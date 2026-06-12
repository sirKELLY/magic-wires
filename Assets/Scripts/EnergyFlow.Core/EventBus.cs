using System;
using System.Collections.Generic;

namespace EnergyFlow.Core
{
    /// <summary>
    /// Typed event bus (spec 7.5). The sim publishes; the host subscribes.
    /// Events are emitted in deterministic order. Subscribers are invoked in
    /// subscription order and can never affect simulation behavior.
    /// </summary>
    public sealed class EventBus
    {
        private readonly Dictionary<Type, List<(Delegate original, Action<ISimEvent> wrapper)>> _subscribers =
            new Dictionary<Type, List<(Delegate, Action<ISimEvent>)>>();

        public void Subscribe<T>(Action<T> handler) where T : ISimEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!_subscribers.TryGetValue(typeof(T), out var list))
            {
                list = new List<(Delegate, Action<ISimEvent>)>();
                _subscribers[typeof(T)] = list;
            }
            list.Add((handler, e => handler((T)e)));
        }

        public void Unsubscribe<T>(Action<T> handler) where T : ISimEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!_subscribers.TryGetValue(typeof(T), out var list)) return;
            int index = list.FindIndex(entry => entry.original.Equals(handler));
            if (index >= 0) list.RemoveAt(index);
        }

        public void Publish(ISimEvent evt)
        {
            if (!_subscribers.TryGetValue(evt.GetType(), out var list)) return;
            // Copy so a handler that subscribes/unsubscribes mid-publish cannot
            // affect this publish's invocation list.
            var snapshot = list.ToArray();
            foreach (var entry in snapshot) entry.wrapper(evt);
        }
    }
}
