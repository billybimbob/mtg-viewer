# mtg-viewer

Magic: The Gathering Card Manager and Deck Builder, using [ASP.net](https://dotnet.microsoft.com/apps/aspnet)

## Requirements

* [.NET 5.0 SDK](https://dotnet.microsoft.com/download)
* Entity Framework Core

### Instal EF Core

```powershell
dotnet tool install --global dotnet-ef
```

## Projects

There are multiple projects in the repository:

* `MTGViewer`: MTG card website and database information
* `MTGViewer.Tests`: test cases for website components

All the ef core and database commands are in reference to the `MTGViewer` project, which should be specified with the `-p` argument.

## Database

The development database is sqlite, where the database is hosted on the local machine, and is not synchronized with the repo.

### Add Database Migrations and Schema

 For the `migrations add` commands, the out directory is recommended to be specified, using the `-o` argument. If the out directory is not specified, then the default target will be the `Migrations` folder.

The mains steps are to create the database schema with ef core:

1. Add/create the database migrations

    ```powershell
    dotnet ef migrations add AddCards -p MTGViewer
    ```

2. Apply/update the database migrations to the actual database

    ```powershell
    dotnet ef database update -p MTGViewer
    ```

### Reset Database

If the schema is modified, the best approach is to just drop all of the previous tables and rebuild the database.

1. Drop the database:

    ```powershell
    dotnet ef database drop -p MTGViewer
    ```

2. Delete the  files in the `Migrations` folder

    ```powershell
    rm -r MTGViewer\Migrations
    ```

3. Repeat the migration and update steps [above](#add-database-migrations-and-schema)

## Run the Application

In the project directory:

```powershell
dotnet watch run -p MTGViewer
```
