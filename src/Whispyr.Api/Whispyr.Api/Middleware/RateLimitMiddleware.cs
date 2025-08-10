using StackExchange.Redis;

namespace Whispyr.Api.Middleware;

public class RateLimitMiddleware(RequestDelegate next, IConnectionMultiplexer mux)
{
    public async Task Invoke(HttpContext ctx)
    {
        // Sadece mesaj POST'unu sınırlayalım: /rooms/{code}/messages  +  POST
        if (ctx.Request.Path.StartsWithSegments("/rooms") &&
            ctx.Request.Path.Value!.EndsWith("/messages", StringComparison.OrdinalIgnoreCase) &&
            HttpMethods.IsPost(ctx.Request.Method))
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var key = $"rl:{ip}:msg";
            var db = mux.GetDatabase();

            // 1 dakika pencerede 60 istek
            var cnt = await db.StringIncrementAsync(key);
            if (cnt == 1) await db.KeyExpireAsync(key, TimeSpan.FromMinutes(1));

            if (cnt > 60)
            {
                var ttl = await db.KeyTimeToLiveAsync(key) ?? TimeSpan.FromMinutes(1);
                var resetAt = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();

                ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                ctx.Response.Headers["Retry-After"] = Math.Ceiling(ttl.TotalSeconds).ToString();
                ctx.Response.Headers["X-RateLimit-Limit"] = "60";
                ctx.Response.Headers["X-RateLimit-Remaining"] = "0";
                ctx.Response.Headers["X-RateLimit-Reset"] = resetAt.ToString();

                await ctx.Response.WriteAsJsonAsync(new { error = "rate_limited", window = "60s", limit = 60, retryInSeconds = (int)Math.Ceiling(ttl.TotalSeconds) });
                  return;
            }
            else
              {
                // başarılı taleplerde kalan hakkı da set edelim (opsiyonel)
               var remaining = Math.Max(0, 60 - (int)cnt);
               ctx.Response.Headers["X-RateLimit-Limit"] = "60";
               ctx.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
              }
        }

        await next(ctx);
    }
}
