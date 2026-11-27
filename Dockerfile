# שלב 1: בנייה - משתמשים ב-image שיש בו את ה-SDK המלא של .NET
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# קודם מעתיקים רק את קובץ הפרויקט ומורידים פקג'ים (כך Docker שומר את השלב הזה ב-cache)
COPY TodoApi.csproj .
RUN dotnet restore

# מעתיקים את שאר הקוד ומפרסמים גרסת Release
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# שלב 2: הרצה - image קטן יותר שיש בו רק את מה שצריך כדי להריץ
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# ה-API מאזין בפורט 8080 (ברירת המחדל של .NET 8 ומעלה), ו-Render מזהה אותו אוטומטית
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER app
ENTRYPOINT ["dotnet", "TodoApi.dll"]
