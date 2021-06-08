# mtg-viewer

Magic: The Gathering Card Manager and Deck Builder, using [ASP.net](https://dotnet.microsoft.com/apps/aspnet)

## Requirements

* [.NET Core SDK](https://dotnet.microsoft.com/download)
* Entity Framework Core

### Instal EF Core

```powershell
dotnet tool install --global dotnet-ef
```

## Database

### Add Database Migrations and Schema

```powershell
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### Reset Database

If the schema is modified, the best approach is to just drop all of the previous tables and rebuild the database.

1. Delete the `Migrations` folder
2. Run the following lines:

```powershell
dotnet ef database drop
dotnet ef migrations add InitialCreate
dotnet ef database update
```

## Run the Application

```powershell
dotnet watch run
```
