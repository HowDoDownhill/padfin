using System;
using System.Collections.Generic;

namespace Padfin;

// Jellyfin DTOs. Field names match the server's JSON. Only the fields Padfin reads are declared.

public class PublicSystemInfo
{
    public string ServerName;
    public string Version;
    public string Id;
    public string ProductName;
}

public class AuthResult
{
    public UserDto User;
    public string AccessToken;
    public string ServerId;
}

public class UserDto
{
    public string Id;
    public string Name;
    public bool HasPassword;
    public string PrimaryImageTag;
}

public class QuickConnectResult
{
    public bool Authenticated;
    public string Secret;
    public string Code;
}

public class ItemsResult
{
    public List<BaseItem> Items = new();
    public int TotalRecordCount;
}

public class UserItemData
{
    public long PlaybackPositionTicks;
    public double? PlayedPercentage;
    public bool Played;
    public bool IsFavorite;
    public int? UnplayedItemCount;
}

public class BaseItem
{
    public string Id;
    public string Name;
    public string Type;
    public string CollectionType;
    public string MediaType;
    public string Overview;
    public string OfficialRating;
    public string SeriesName;
    public string SeriesId;
    public string SeasonId;
    public string SeasonName;
    public string ParentId;
    public string ParentBackdropItemId;
    public string SeriesPrimaryImageTag;
    public string Container;
    public int? IndexNumber;
    public int? ParentIndexNumber;
    public int? ProductionYear;
    public int? ChildCount;
    public long? RunTimeTicks;
    public float? CommunityRating;
    public bool? IsFolder;
    public Dictionary<string, string> ImageTags;
    public List<string> BackdropImageTags;
    public List<string> ParentBackdropImageTags;
    public List<string> Genres;
    public List<string> Taglines;
    public UserItemData UserData;
    public List<MediaSource> MediaSources;
    public List<Chapter> Chapters;
    /// <summary>Scrubbing thumbnails, keyed by media source id and then by thumbnail width.</summary>
    public Dictionary<string, Dictionary<string, TrickplayInfo>> Trickplay;

    public bool IsEpisode => Type == "Episode";
    public bool IsPlayable => Type is "Movie" or "Episode" or "Video" or "MusicVideo" or "Trailer" or "Recording";
    public bool IsContainer => Type is "CollectionFolder" or "UserView" or "Folder" or "BoxSet" or "Playlist" or "AggregateFolder";
    public bool HasPrimary => ImageTags != null && ImageTags.ContainsKey("Primary");
    public bool HasBackdrop => BackdropImageTags is { Count: > 0 };
    public long ResumeTicks => UserData?.PlaybackPositionTicks ?? 0;

    /// <summary>"S1:E4" for an episode, empty otherwise.</summary>
    public string EpisodeCode =>
        IsEpisode && IndexNumber != null ? $"S{ParentIndexNumber ?? 1}:E{IndexNumber}" : "";

    public string RuntimeText
    {
        get
        {
            if (RunTimeTicks is not > 0) { return ""; }
            var span = TimeSpan.FromTicks(RunTimeTicks.Value);
            return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{Math.Max(1, span.Minutes)}m";
        }
    }
}

public class Chapter
{
    public long StartPositionTicks;
    public string Name;
}

/// <summary>One size of scrubbing thumbnails. They come as sheets of TileWidth by TileHeight thumbnails.</summary>
public class TrickplayInfo
{
    public int Width;
    public int Height;
    public int TileWidth;
    public int TileHeight;
    public int ThumbnailCount;
    /// <summary>Milliseconds between thumbnails.</summary>
    public int Interval;
}

public class MediaSource
{
    public string Id;
    public string Name;
    public string Container;
    public string TranscodingUrl;
    public bool SupportsDirectPlay;
    public bool SupportsDirectStream;
    public bool SupportsTranscoding;
    // The tracks the server picked from the user's language and subtitle preferences.
    public int? DefaultAudioStreamIndex;
    public int? DefaultSubtitleStreamIndex;
    public List<MediaStream> MediaStreams = new();
}

public class MediaStream
{
    public int Index;
    public string Type;
    public string Codec;
    public string Language;
    public string DisplayTitle;
    public string Title;
    public bool IsExternal;
    public bool IsTextSubtitleStream;
    public bool IsDefault;
}

public class PlaybackInfo
{
    public List<MediaSource> MediaSources = new();
    public string PlaySessionId;
    public string ErrorCode;
}

public class MediaSegmentResult
{
    public List<MediaSegment> Items = new();
}

public class MediaSegment
{
    public string Type;
    public long StartTicks;
    public long EndTicks;
}

public class DiscoveredServer
{
    public string Address;
    public string Id;
    public string Name;
}
