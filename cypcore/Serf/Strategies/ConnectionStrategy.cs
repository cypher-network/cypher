using System;
using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace CYPCore.Serf.Strategies
{
    public abstract class ConnectionStrategy
    {
        private readonly Subject<bool> _reconnect = new();
        public int? MaxNumberOfAttempts { get; }
        public int AttemptCounter { get; private set; }

        public Subject<bool> Reconnect => _reconnect;

        protected ConnectionStrategy(int? maxNumberOfAttempts)
        {
            MaxNumberOfAttempts = maxNumberOfAttempts;
            ResetReconnectCounter();
        }

        public void ResetReconnectCounter()
        {
            AttemptCounter = 0;
        }

        public void DoReconnect()
        {
            if (MaxNumberOfAttempts != null && AttemptCounter == MaxNumberOfAttempts)
            {
                Reconnect.OnError(new Exception("Max. retries"));
            }

            StrategyImplementation(AttemptCounter++);
        }

        protected abstract void StrategyImplementation(int numberOfAttempts);
    }

    public class DelayConnectionStrategy : ConnectionStrategy
    {
        private readonly TimeSpan _delay;
        
        public DelayConnectionStrategy(long delaySeconds)
            :base(null)
        {
            _delay = TimeSpan.FromSeconds(delaySeconds);
        }

        public DelayConnectionStrategy(long delaySeconds, int maxNumberOfAttempts)
            :base(maxNumberOfAttempts)
        {
            _delay = TimeSpan.FromSeconds(delaySeconds);
        }
        
        protected override void StrategyImplementation(int numberOfAttempts)
        {
            Task.Delay(_delay).Wait();
            Reconnect.OnNext(true);
        }
    }

    public class ExponentialBackoffConnectionStrategy : ConnectionStrategy
    {
        private readonly TimeSpan _baseTime;
        private TimeSpan _currentDelay = TimeSpan.Zero;
        
        public ExponentialBackoffConnectionStrategy(TimeSpan baseTime)
            : base(null)
        {
            _baseTime = baseTime;
        }

        public ExponentialBackoffConnectionStrategy(TimeSpan baseTime, int maxNumberOfAttempts)
            :base(maxNumberOfAttempts)
        {
            _baseTime = baseTime;
        }

        protected override void StrategyImplementation(int numberOfAttempts)
        {
            if (numberOfAttempts <= 1)
            {
                _currentDelay = _baseTime;
            }
            else
            {
                _currentDelay *= 2;
            }
            
            Task.Delay(_currentDelay).Wait();
            Reconnect.OnNext(true);
        }
    }
}