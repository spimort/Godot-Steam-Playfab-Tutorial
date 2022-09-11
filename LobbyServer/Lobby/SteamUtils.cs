public class SteamUserInfoResponse {
    public SteamUserInfoResponseData? Response { get;set; }
}

public class SteamUserInfoResponseData {
    public SteamUserInfoResult? Params { get;set; }
}

public class SteamUserInfoResult {
    public string? Result { get;set; }
    public string? Steamid { get;set; }
    public bool Vacbanned { get;set; }
    public bool Publisherbanned { get;set; }
}

public class SteamPlayerSummariesResponse {
    public SteamPlayerSummariesResponseData? Response { get;set; }
}

public class SteamPlayerSummariesResponseData {
    public List<SteamPlayerSummary>? Players { get;set; }
}

public class SteamPlayerSummary {
    public string? Personaname { get;set; }
    public string? Avatar { get;set; }
}
