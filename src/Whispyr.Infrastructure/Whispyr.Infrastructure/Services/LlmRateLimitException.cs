using System;

namespace Whispyr.Infrastructure.Services
{
    public sealed class LlmRateLimitException : Exception
    {
        public int? RetryAfterSeconds { get; }

        public LlmRateLimitException(string message, int? retryAfterSeconds = null, Exception? inner = null)
            : base(message, inner)
        {
            RetryAfterSeconds = retryAfterSeconds;
        }
    }
}
