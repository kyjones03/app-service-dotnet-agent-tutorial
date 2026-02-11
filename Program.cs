using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? Environment.MachineName;
bool injectError = Environment.GetEnvironmentVariable("INJECT_ERROR") == "1";

// CRITICAL: AFD session affinity requires Cache-Control: no-store
// Without this header, AFD treats responses as cacheable and won't set the ASLBSA affinity cookie
app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

// API endpoint - returns JSON, no page navigation
app.MapGet("/api/increment", (HttpContext context) =>
{
    int pressCount = 0;
    if (context.Request.Cookies.TryGetValue("crashCount", out var cookieVal))
        int.TryParse(cookieVal, out pressCount);

    bool safeMode = context.Request.Query.ContainsKey("safe");

    if (safeMode)
        pressCount = 0;
    else
        pressCount++;

    context.Response.Cookies.Append("crashCount", pressCount.ToString(), new CookieOptions
    {
        Expires = DateTimeOffset.Now.AddHours(1),
        Path = "/",
        SameSite = SameSiteMode.Lax
    });

    if (injectError && !safeMode && pressCount > 5)
        throw new Exception("Simulated error after 5 button clicks!");

    return Results.Json(new { count = pressCount, instance = siteName });
});

// API endpoint - reset counter
app.MapGet("/api/reset", (HttpContext context) =>
{
    context.Response.Cookies.Append("crashCount", "0", new CookieOptions
    {
        Expires = DateTimeOffset.Now.AddHours(1),
        Path = "/",
        SameSite = SameSiteMode.Lax
    });

    return Results.Json(new { count = 0, instance = siteName });
});

// API endpoint - get current state without changing anything
app.MapGet("/api/status", (HttpContext context) =>
{
    int pressCount = 0;
    if (context.Request.Cookies.TryGetValue("crashCount", out var cookieVal))
        int.TryParse(cookieVal, out pressCount);

    return Results.Json(new { count = pressCount, instance = siteName });
});

// Main page - static HTML with JavaScript fetch calls
app.MapGet("/", async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";

    await context.Response.WriteAsync($@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>.NET Button Click Demo</title>
    <style>
        body {{
            background: #f8fafc;
            font-family: 'Segoe UI', Arial, sans-serif;
            text-align: center;
            margin: 0; padding: 0;
        }}
        .container {{
            margin-top: 80px;
            background: #fff;
            border-radius: 18px;
            box-shadow: 0 6px 24px rgba(0,0,0,0.08);
            display: inline-block;
            padding: 40px 36px 36px 36px;
        }}
        .instance {{
            font-size: 0.95em;
            color: #64748b;
            margin-bottom: 8px;
        }}
        .instance b {{
            color: #334155;
        }}
        .number {{
            font-size: 3.2em;
            color: #2563eb;
            margin-bottom: 18px;
            transition: transform 0.15s;
        }}
        .number.bump {{
            transform: scale(1.15);
        }}
        .note {{
            margin-top: 12px;
            color: #ad6800;
            font-size: 1em;
        }}
        .warning {{
            margin-top: 30px;
            color: #b91c1c;
            font-weight: bold;
            font-size: 1.3em;
        }}
        .btn-primary {{
            margin-top: 30px;
            background: #16187c;
            color: #fff;
            border: none;
            border-radius: 6px;
            font-size: 1.2em;
            padding: 12px 28px;
            cursor: pointer;
            transition: background 0.2s;
        }}
        .btn-primary:hover {{
            background: #582912;
        }}
        .btn-reset {{
            margin-top: 16px;
            background: #2563eb;
            color: #fff;
            border: none;
            border-radius: 6px;
            font-size: 1em;
            padding: 8px 22px;
            cursor: pointer;
        }}
        .btn-reset:hover {{
            background: #1e40af;
        }}
        .history {{
            margin-top: 20px;
            font-size: 0.85em;
            color: #94a3b8;
        }}
        .history span {{
            margin: 0 4px;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='instance'>Instance: <b id='instanceName'>{siteName}</b></div>
        <div class='number' id='counter'>0</div>
        <div>
            <button class='btn-primary' id='incrementBtn' onclick='increment()'>Increment</button>
        </div>
        <div>
            <button class='btn-reset' onclick='resetCounter()'>Reset Counter</button>
        </div>
        <div class='history' id='history'></div>
        {(injectError ? "<div class='note' id='noteArea'>Error injection enabled — error triggers after 5 clicks.</div>" : "")}
        {(injectError ? "<div class='warning'>ERROR INJECTION ENABLED: Simulated error will occur after 5 clicks.</div>" : "")}
        <div class='note'>Note: For the demo to work, set app setting <b>INJECT_ERROR=1</b> on the slot you want to simulate errors!</div>
    </div>

    <script>
        let instanceHistory = [];

        // Load current state on page load
        window.addEventListener('DOMContentLoaded', async () => {{
            try {{
                const resp = await fetch('/api/status', {{ credentials: 'same-origin' }});
                const data = await resp.json();
                document.getElementById('counter').textContent = data.count;
                document.getElementById('instanceName').textContent = data.instance;
                trackInstance(data.instance);
            }} catch (e) {{
                console.error('Failed to load status:', e);
            }}
        }});

        async function increment() {{
            try {{
                const resp = await fetch('/api/increment', {{ credentials: 'same-origin' }});
                const data = await resp.json();
                document.getElementById('counter').textContent = data.count;
                document.getElementById('instanceName').textContent = data.instance;
                trackInstance(data.instance);

                // Visual bump animation
                const el = document.getElementById('counter');
                el.classList.add('bump');
                setTimeout(() => el.classList.remove('bump'), 150);
            }} catch (e) {{
                console.error('Error:', e);
                document.getElementById('counter').textContent = 'Error!';
            }}
        }}

        async function resetCounter() {{
            try {{
                const resp = await fetch('/api/reset', {{ credentials: 'same-origin' }});
                const data = await resp.json();
                document.getElementById('counter').textContent = data.count;
                document.getElementById('instanceName').textContent = data.instance;
                instanceHistory = [];
                updateHistory();
                trackInstance(data.instance);
            }} catch (e) {{
                console.error('Error:', e);
            }}
        }}

        function trackInstance(name) {{
            instanceHistory.push(name);
            // Keep last 20 entries
            if (instanceHistory.length > 20) instanceHistory.shift();
            updateHistory();
        }}

        function updateHistory() {{
            const el = document.getElementById('history');
            if (instanceHistory.length < 2) {{
                el.textContent = '';
                return;
            }}
            el.innerHTML = 'Instance history: ' + instanceHistory.map(n => '<span>' + n + '</span>').join(' → ');
        }}
    </script>
</body>
</html>
    ");
});

app.Run();