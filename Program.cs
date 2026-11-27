using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using TodoApi;

var builder = WebApplication.CreateBuilder(args);

// הזרקת ה-Context ל-Services, כדי שכל route יוכל לקבל אותו כפרמטר
builder.Services.AddDbContext<ToDoDbContext>();

// CORS - הרשאה כללית לכל קליינט לפנות ל-API
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// JWT - הגדרות האימות: איך לבדוק שהטוקן שהקליינט שולח תקין
var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!));
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = jwtKey
        };
    });
builder.Services.AddAuthorization();

// כלי מובנה של ASP.NET להצפנת סיסמאות (hash) - לא שומרים סיסמה גלויה במסד
var passwordHasher = new PasswordHasher<User>();

// Swagger - ממשק לבדיקת ה-API, כולל כפתור Authorize להדבקת טוקן
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "הדביקי כאן את הטוקן שקיבלת מ-login"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

app.UseCors();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
    options.RoutePrefix = string.Empty; // Swagger ייפתח בכתובת הראשית
});

app.UseAuthentication();
app.UseAuthorization();

// פונקציה שיוצרת טוקן JWT עבור משתמש
string CreateToken(User user)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username)
    };

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(2),
        signingCredentials: new SigningCredentials(jwtKey, SecurityAlgorithms.HmacSha256));

    return new JwtSecurityTokenHandler().WriteToken(token);
}

// ===== הזדהות =====

// הרשמה - יצירת משתמש חדש
app.MapPost("/register", async (ToDoDbContext db, LoginRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("יש למלא שם משתמש וסיסמה");

    if (await db.Users.AnyAsync(u => u.Username == request.Username))
        return Results.Conflict("שם המשתמש כבר תפוס");

    var user = new User { Username = request.Username };
    user.Password = passwordHasher.HashPassword(user, request.Password);

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Ok(new { token = CreateToken(user) });
});

// התחברות - בדיקת שם משתמש וסיסמה והחזרת טוקן
app.MapPost("/login", async (ToDoDbContext db, LoginRequest request) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
    if (user is null)
        return Results.Unauthorized();

    var result = passwordHasher.VerifyHashedPassword(user, user.Password, request.Password);
    if (result == PasswordVerificationResult.Failed)
        return Results.Unauthorized();

    return Results.Ok(new { token = CreateToken(user) });
});

// ===== משימות - כולן דורשות טוקן תקין (RequireAuthorization) =====

// 1. שליפת כל המשימות
app.MapGet("/items", async (ToDoDbContext db) =>
    await db.Items.ToListAsync())
    .RequireAuthorization();

// 2. הוספת משימה חדשה
app.MapPost("/items", async (ToDoDbContext db, Item item) =>
{
    db.Items.Add(item);
    await db.SaveChangesAsync();
    return Results.Created($"/items/{item.Id}", item);
}).RequireAuthorization();

// 3. עדכון משימה
app.MapPut("/items/{id}", async (ToDoDbContext db, int id, Item input) =>
{
    var item = await db.Items.FindAsync(id);
    if (item is null) return Results.NotFound();

    if (input.Name is not null) item.Name = input.Name;
    item.IsComplete = input.IsComplete;
    await db.SaveChangesAsync();
    return Results.Ok(item);
}).RequireAuthorization();

// 4. מחיקת משימה
app.MapDelete("/items/{id}", async (ToDoDbContext db, int id) =>
{
    var item = await db.Items.FindAsync(id);
    if (item is null) return Results.NotFound();

    db.Items.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.Run();

// המבנה של הנתונים שהקליינט שולח בהרשמה ובהתחברות
record LoginRequest(string Username, string Password);
