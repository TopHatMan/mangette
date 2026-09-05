namespace API.Controllers.Requests;

public sealed record SetProwlarrRecord(string Url, string ApiKey);
public sealed record SetQBittorrentRecord(string Url, string Username, string Password);
public sealed record SetSabnzbdRecord(string Url, string ApiKey);
public sealed record SetComicVineRecord(string ApiKey);
