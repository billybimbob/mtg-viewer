# mtg-viewer

Magic: The Gathering Card Manager and Deck Builder, using [ASP.net](https://dotnet.microsoft.com/apps/aspnet)

## Requirements

* [.NET Core 5.0](https://dotnet.microsoft.com/download)
* Entity Framework Core

### Instal EF Core

```powershell
dotnet tool install --global dotnet-ef
```

## Add Database Migrations and Schema

```powershell
dotnet ef migrations add InitialCreate
dotnet ef database update
```

## Run the Application

```powershell
dotnet watch run
```
