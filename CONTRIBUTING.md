## Contributing

If you want to contribute, please feel free to fork and create a Pull-Request!

### General rules (Codestyle)

- Use explicit types for your variables. This improves readability.
    - **DO**
      ```csharp
      Manga[] zyx = Object.GetAnotherThing(); //I can see that zyx is an Array, without digging through more code
      ```
    - **DO _NOT_**
      ```csharp
      var xyz = Object.GetSomething(); //What is xyz? An Array? A string? An object?
      ```

- Indent your `if` and `for` blocks
    - **DO**
      ```csharp
      if(true)
        return false;
      ```
    - **DO _NOT_**
      ```csharp
      if(true) return false;
      ```
      <details>
        <summary>Because try reading this</summary>

        ```csharp
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return s;
        ```

      </details>

- When using shorthand, _this_ improves readability for longer lines (at some point just use if-else...):
```csharp
bool retVal = xyz is true
    ? false
    : true;
```
```csharp
bool retVal = xyz?
    ?? abc?
    ?? true;
```

### If you want to add a new Website-Connector:

1. Copy one of the existing connectors, or start from scratch and inherit from `API.MangaConnectors.MangaConnector`.
2. Add the new Connector as Object-Instance in `Mangette.cs` to the MangaConnector-Array `MangaConnectors`.
3. Add the discriminator to the `MangaContext.cs` `MangaConnector`-Entity

### Database and EF Core

Mangette uses a **code-first** EF Core approach with SQLite (`./data/mangette.db`). If you modify the database schema you need to create a migration.

Useful environment variables:

| variable | default |
| --- | --- |
| `PORT` | `8585` |
| `MANGETTE_HOME` | folder next to the executable |
| `DOWNLOAD_LOCATION` | `./Manga` |
| `FLARESOLVERR_URL` | empty (optional; e.g. `http://192.168.1.210:8181`) |

### A broad overview of where is what:

![Image](DB-Layout.png)

- `Program.cs` Configuration for ASP.NET, Swagger (also in `NamedSwaggerGenOptions.cs`)
- `Mangette.cs` Worker-Logic
- `Schema/**` Entity-Framework Schema Definitions
- `MangaDownloadClients/**` Networking-Clients for Scraping
- `Controllers/**` ASP.NET Controllers (Endpoints)

### How to test locally

```bash
dotnet run --project API/API.csproj
```

Then open http://localhost:8585. Cloudflare bypass uses built-in Chromium (Chrome/Edge if installed).
