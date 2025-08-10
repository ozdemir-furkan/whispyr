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
                ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ctx.Response.WriteAsJsonAsync(new { error = "rate_limited", window = "1m", limit = 60 });
                return;
            }
        }

        await next(ctx);
    }
}
