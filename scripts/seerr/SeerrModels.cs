using System.Collections.Generic;

namespace Padfin;

// Seerr DTOs. Field names match the API's JSON, case aside. Only the fields Padfin reads are declared.

public class SeerrUser
{
    public int Id;
    public string DisplayName;
    public string Email;
    public string Username;
    public string JellyfinUsername;
    public int Permissions;

    public string Name => DisplayName ?? JellyfinUsername ?? Username ?? Email ?? "";
}

public class SeerrPublicSettings
{
    public bool Initialized;
    public string ApplicationTitle;
    public bool CacheImages;
    public bool Movie4kEnabled;
    public bool Series4kEnabled;
}

public class SeerrPage
{
    public int Page;
    public int TotalPages;
    public List<SeerrItem> Results = new();
}

/// <summary>A movie or show from TMDB as Seerr reports it, in a list or in full detail.</summary>
public class SeerrItem
{
    public int Id;
    public string MediaType;
    public string Title;
    public string Name;
    public string Overview;
    public string Tagline;
    public string PosterPath;
    public string BackdropPath;
    public string ReleaseDate;
    public string FirstAirDate;
    public double? VoteAverage;
    public int? Runtime;
    public List<SeerrGenre> Genres;
    public List<SeerrSeason> Seasons;
    public SeerrMedia MediaInfo;

    public bool IsMovie => MediaType == "movie";
    public string DisplayTitle => Title ?? Name ?? "";

    public string Year
    {
        get
        {
            var date = ReleaseDate ?? FirstAirDate;
            return date is { Length: >= 4 } ? date[..4] : "";
        }
    }
}

public class SeerrGenre
{
    public string Name;
}

/// <summary>A Radarr or Sonarr server Seerr sends requests to.</summary>
public class SeerrService
{
    public int Id;
    public string Name;
    public bool Is4k;
    public bool IsDefault;
    public string ActiveDirectory;
    public int? ActiveProfileId;
}

public class SeerrServiceDetails
{
    public SeerrService Server;
    public List<SeerrProfile> Profiles = new();
    public List<SeerrRootFolder> RootFolders = new();
}

public class SeerrProfile
{
    public int Id;
    public string Name;
}

public class SeerrRootFolder
{
    public int Id;
    public string Path;
}

/// <summary>Choices sent with a request. Unset values leave Seerr's defaults in place.</summary>
public class SeerrRequestOptions
{
    public bool Is4k;
    public int? ServerId;
    public int? ProfileId;
    public string RootFolder;
}

public class SeerrSeason
{
    public int SeasonNumber;
    public string Name;
    public int EpisodeCount;
}

/// <summary>Seerr's record of a title: whether it is in the library and who asked for it.</summary>
public class SeerrMedia
{
    public const int Pending = 2;
    public const int Processing = 3;
    public const int PartiallyAvailable = 4;
    public const int Available = 5;

    public int Id;
    public int TmdbId;
    public string MediaType;
    public int Status;
    public int Status4k;
    public string JellyfinMediaId;
    public string JellyfinMediaId4k;
    public List<SeerrRequest> Requests;

    /// <summary>The status of the standard or the 4K copy.</summary>
    public int StatusFor(bool is4k) => is4k ? Status4k : Status;

    public string StatusText => Status switch
    {
        Pending => "Requested",
        Processing => "Processing",
        PartiallyAvailable => "Partially available",
        Available => "Available",
        _ => "",
    };
}

public class SeerrRequest
{
    public const int PendingApproval = 1;
    public const int Approved = 2;
    public const int Declined = 3;
    public const int Failed = 4;
    public const int Completed = 5;

    public int Id;
    public int Status;
    public string Type;
    public bool Is4k;
    public SeerrMedia Media;
    public SeerrUser RequestedBy;
    public List<SeerrRequestSeason> Seasons;

    /// <summary>The state to show, combining approval with how far the download has come.</summary>
    public string StatusText => Status switch
    {
        PendingApproval => "Waiting for approval",
        Declined => "Declined",
        Failed => "Failed",
        _ => Media?.Status switch
        {
            SeerrMedia.Available => "Available",
            SeerrMedia.PartiallyAvailable => "Partially available",
            _ => "Processing",
        },
    };
}

public class SeerrRequestSeason
{
    public int SeasonNumber;
}

public class SeerrRequestPage
{
    public List<SeerrRequest> Results = new();
}

public class SeerrError
{
    public string Message;
    public string Error;
}
