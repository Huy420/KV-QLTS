using System.Security.Claims;
using System.Threading.RateLimiting;
using KieuViet;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AssetDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Assets")));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "KV.Assets";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Write", p => p.RequireRole("Admin", "Inventory"));
    o.AddPolicy("Admin", p => p.RequireRole("Admin"));
});
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 10 * 1024 * 1024);
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (DbUpdateConcurrencyException) { await Error(ctx, 409, "Dữ liệu vừa được người khác cập nhật. Vui lòng tải lại."); }
    catch (DbUpdateException ex) { app.Logger.LogWarning(ex, "Database write failed"); await Error(ctx, 409, "Không thể lưu. Kiểm tra mã trùng hoặc dữ liệu liên quan."); }
    catch (AntiforgeryValidationException) { await Error(ctx, 400, "Phiên bảo mật hết hạn. Vui lòng tải lại trang."); }
    catch (BadHttpRequestException ex) { await Error(ctx, ex.StatusCode, "Dữ liệu gửi lên không hợp lệ hoặc vượt dung lượng cho phép."); }
    catch (Exception ex) when (ex is not OperationCanceledException)
    { app.Logger.LogError(ex, "Request failed"); await Error(ctx, 500, "Không thể xử lý yêu cầu. Vui lòng kiểm tra kết nối SQL Server hoặc liên hệ quản trị."); }
});
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
        await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx);
    await next();
});

// Explicit initialization only: never change a production schema on ordinary startup.
if (args.Contains("--init-db"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AssetDb>();
    var password = Environment.GetEnvironmentVariable("KV_ADMIN_PASSWORD");
    if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || password.Length > 256)
        throw new InvalidOperationException("Đặt KV_ADMIN_PASSWORD từ 12 đến 256 ký tự trước khi khởi tạo.");
    await db.Database.EnsureCreatedAsync();
    if (!await db.Users.AnyAsync())
    {
        db.Users.Add(new AppUser { Username = "admin", DisplayName = "Quản trị viên", Role = "Admin", PasswordHash = Passwords.Hash(password) });
        await db.SaveChangesAsync();
    }
    app.Logger.LogInformation("Database initialized. Run again without --init-db to serve.");
    return;
}
app.MapGet("/api/csrf", (HttpContext c, IAntiforgery a) => Results.Ok(new { token = a.GetAndStoreTokens(c).RequestToken }));
app.MapPost("/api/login", async (LoginInput input, AssetDb db, HttpContext c) =>
{
    if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrEmpty(input.Password) || input.Password.Length > 256) return Results.BadRequest(new { error = "Nhập tài khoản và mật khẩu." });
    var user = await db.Users.SingleOrDefaultAsync(x => x.Username == input.Username.Trim());
    if (user is null || !Passwords.Verify(input.Password, user.PasswordHash)) return Results.Json(new { error = "Tài khoản hoặc mật khẩu không đúng." }, statusCode: 401);
    var identity = new ClaimsIdentity([new(ClaimTypes.Name, user.Username), new(ClaimTypes.Role, user.Role), new("displayName", user.DisplayName)], CookieAuthenticationDefaults.AuthenticationScheme);
    await c.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Ok(new { user.Username, user.DisplayName, user.Role });
}).RequireRateLimiting("login");
app.MapPost("/api/logout", async (HttpContext c) => { await c.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
app.MapGet("/api/me", (HttpContext c) => Results.Ok(new { username = c.User.Identity!.Name, displayName = c.User.FindFirstValue("displayName"), role = c.User.FindFirstValue(ClaimTypes.Role) })).RequireAuthorization();

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/assets", async (AssetDb db) => await db.Assets.AsNoTracking().Where(x => !x.Archived).OrderByDescending(x => x.Id).ToListAsync());
api.MapPost("/assets", async (AssetInput input, AssetDb db, HttpContext c) =>
{
    var error = Rules.Validate(input);
    if (error is not null) return Results.BadRequest(new { error });
    var asset = new Asset(); Rules.Apply(asset, input); db.Assets.Add(asset);
    Rules.Audit(db, c, "Thêm tài sản", input.Code); await db.SaveChangesAsync();
    return Results.Created($"/api/assets/{asset.Id}", asset);
}).RequireAuthorization("Write");
api.MapPut("/assets/{id:int}", async (int id, AssetInput input, AssetDb db, HttpContext c) =>
{
    var error = Rules.Validate(input); if (error is not null) return Results.BadRequest(new { error });
    var asset = await db.Assets.SingleOrDefaultAsync(x => x.Id == id && !x.Archived);
    if (asset is null) return Results.NotFound();
    if (!Rules.VersionMatches(input.Version, asset.Version)) return Results.Conflict(new { error = "Dữ liệu đã thay đổi. Tải lại trước khi sửa." });
    Rules.Apply(asset, input); Rules.Audit(db, c, "Sửa tài sản", input.Code); await db.SaveChangesAsync();
    return Results.Ok(asset);
}).RequireAuthorization("Write");
api.MapDelete("/assets/{id:int}", async (int id, string version, AssetDb db, HttpContext c) =>
{
    var asset = await db.Assets.FindAsync(id); if (asset is null || asset.Archived) return Results.NotFound();
    if (!Rules.VersionMatches(version, asset.Version)) return Results.Conflict(new { error = "Dữ liệu đã thay đổi. Vui lòng tải lại." });
    asset.Archived = true; Rules.Audit(db, c, "Lưu trữ tài sản", asset.Code); await db.SaveChangesAsync(); return Results.NoContent();
}).RequireAuthorization("Admin");
api.MapGet("/sessions", async (AssetDb db) => await db.Sessions.AsNoTracking().OrderByDescending(x => x.Id).Select(x => new
{
    x.Id, x.Name, x.Department, x.CreatedAt, x.ClosedAt,
    Total = db.Items.Count(i => i.SessionId == x.Id),
    Checked = db.Items.Count(i => i.SessionId == x.Id && i.Result != "Chưa kiểm kê"),
    Differences = db.Items.Count(i => i.SessionId == x.Id && i.Result != "Chưa kiểm kê" && (i.Result != "Khớp" || i.ActualLocation != i.ExpectedLocation))
}).ToListAsync());
api.MapPost("/sessions", async (SessionInput input, AssetDb db, HttpContext c) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 250 || input.Department is null || input.Department.Length > 150)
        return Results.BadRequest(new { error = "Tên đợt bắt buộc, tối đa 250 ký tự; phòng ban tối đa 150 ký tự." });
    var assets = await db.Assets.Where(x => !x.Archived && (input.Department == "" || x.Department == input.Department)).ToListAsync();
    if (assets.Count == 0) return Results.BadRequest(new { error = "Không có tài sản trong phạm vi kiểm kê." });
    var session = new InventorySession { Name = input.Name.Trim(), Department = input.Department };
    db.Sessions.Add(session);
    foreach (var a in assets) db.Items.Add(new InventoryItem { Session = session, AssetId = a.Id, Code = a.Code, Name = a.Name, ExpectedLocation = a.Location, ExpectedCustodian = a.Custodian });
    Rules.Audit(db, c, "Mở đợt kiểm kê", input.Name); await db.SaveChangesAsync(); return Results.Ok(new { session.Id });
}).RequireAuthorization("Write");
api.MapGet("/sessions/{id:int}/items", async (int id, AssetDb db) => await db.Items.AsNoTracking().Where(x => x.SessionId == id).OrderBy(x => x.Code).Select(x => new
{ x.Id, x.AssetId, x.Code, x.Name, x.ExpectedLocation, x.ExpectedCustodian, x.ActualLocation, x.Result, x.Notes, x.CheckedAt, x.CheckedBy, x.Version }).ToListAsync());
api.MapPut("/sessions/{id:int}/items/{itemId:int}", async (int id, int itemId, CheckInput input, AssetDb db, HttpContext c) =>
{
    if (!Rules.Results.Contains(input.Result) || input.ActualLocation is null || input.ActualLocation.Length > 150 || input.Notes is null || input.Notes.Length > 2000)
        return Results.BadRequest(new { error = "Kết quả, vị trí hoặc ghi chú không hợp lệ." });
    // Serializable transaction makes closing and recording mutually exclusive.
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    var session = await db.Sessions.FindAsync(id);
    if (session is null) return Results.NotFound();
    if (session.ClosedAt is not null) return Results.Conflict(new { error = "Đợt kiểm kê đã khóa." });
    var item = await db.Items.SingleOrDefaultAsync(x => x.SessionId == id && x.Id == itemId);
    if (item is null) return Results.NotFound();
    if (!Rules.VersionMatches(input.Version, item.Version)) return Results.Conflict(new { error = "Kết quả vừa được người khác cập nhật. Vui lòng tải lại." });
    item.Result = input.Result; item.ActualLocation = input.ActualLocation.Trim(); item.Notes = input.Notes.Trim();
    item.CheckedAt = input.Result == "Chưa kiểm kê" ? null : DateTime.UtcNow;
    item.CheckedBy = c.User.Identity!.Name!;
    Rules.Audit(db, c, "Ghi nhận kiểm kê", $"Đợt {id}: {item.Code} - {item.Result}");
    await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok();
}).RequireAuthorization("Write");
api.MapPost("/sessions/{id:int}/close", async (int id, AssetDb db, HttpContext c) =>
{
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    var session = await db.Sessions.FindAsync(id); if (session is null) return Results.NotFound();
    if (session.ClosedAt is not null) return Results.Conflict(new { error = "Đợt này đã khóa." });
    if (await db.Items.AnyAsync(x => x.SessionId == id && x.Result == "Chưa kiểm kê")) return Results.BadRequest(new { error = "Cần kiểm kê toàn bộ tài sản trước khi khóa đợt." });
    session.ClosedAt = DateTime.UtcNow; Rules.Audit(db, c, "Khóa đợt kiểm kê", session.Name);
    await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok();
}).RequireAuthorization("Admin");
api.MapGet("/audit", async (AssetDb db) => await db.Audit.AsNoTracking().OrderByDescending(x => x.Id).Take(200).ToListAsync()).RequireAuthorization("Admin");
api.MapGet("/users", async (AssetDb db) => await db.Users.Select(x => new { x.Id, x.Username, x.DisplayName, x.Role }).ToListAsync()).RequireAuthorization("Admin");
api.MapPost("/users", async (UserInput input, AssetDb db, HttpContext c) =>
{
    if (string.IsNullOrWhiteSpace(input.Username) || input.Username.Length > 100 || string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Length > 150 || input.Password is null || input.Password.Length < 12 || input.Password.Length > 256 || !new[] { "Admin", "Inventory", "Viewer" }.Contains(input.Role))
        return Results.BadRequest(new { error = "Kiểm tra thông tin. Mật khẩu cần từ 12 đến 256 ký tự." });
    db.Users.Add(new AppUser { Username = input.Username.Trim(), DisplayName = input.DisplayName.Trim(), Role = input.Role, PasswordHash = Passwords.Hash(input.Password) });
    Rules.Audit(db, c, "Tạo tài khoản", input.Username); await db.SaveChangesAsync(); return Results.Ok();
}).RequireAuthorization("Admin");
ExcelEndpoints.Map(api);
app.Run();

static async Task Error(HttpContext c, int status, string error)
{
    if (c.Response.HasStarted) return;
    c.Response.StatusCode = status; await c.Response.WriteAsJsonAsync(new { error });
}
