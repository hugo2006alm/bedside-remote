namespace BedsideRemote.Core;

public sealed record QueueEntry(string EntryId, string VideoId, string Url, string Title);
public sealed record QueueSnapshot(List<QueueEntry> Queue, string? CurrentEntryId);

public sealed class PlaybackQueue
{
    public List<QueueEntry> Items { get; } = [];
    public string? CurrentEntryId { get; private set; }
    private bool wasPlaying;
    private string? observedSession;
    private string? observedVideo;
    private string? pendingVideo;
    public PlaybackQueue(QueueSnapshot? snapshot = null)
    {
        if (snapshot == null) return;
        foreach (var entry in snapshot.Queue.Take(50))
            if (Protocol.YoutubeId(entry.Url) == entry.VideoId && Items.All(x => x.EntryId != entry.EntryId)) Items.Add(entry);
        CurrentEntryId = Items.Any(x => x.EntryId == snapshot.CurrentEntryId) ? snapshot.CurrentEntryId : null;
    }
    public QueueSnapshot Snapshot() => new([.. Items], CurrentEntryId);
    public QueueEntry Enqueue(string url, string mode)
    {
        if (Items.Count >= 50) throw new InvalidOperationException("Queue is full (50 videos). Remove an entry first.");
        var video = Protocol.YoutubeId(url);
        var entry = new QueueEntry(Guid.NewGuid().ToString(), video, $"https://www.youtube.com/watch?v={video}", video);
        var current = Items.FindIndex(x => x.EntryId == CurrentEntryId);
        if (mode == "next") Items.Insert(current < 0 ? 0 : current + 1, entry);
        else Items.Add(entry);
        if (mode == "now") Select(entry);
        return entry;
    }
    public void Select(QueueEntry entry)
    {
        CurrentEntryId = entry.EntryId;
        pendingVideo = entry.VideoId;
        ResetObservation();
    }
    public QueueEntry? Step(int direction)
    {
        var index = Items.FindIndex(x => x.EntryId == CurrentEntryId);
        var next = index < 0 ? (direction > 0 ? 0 : -1) : index + direction;
        if (next < 0 || next >= Items.Count) return null;
        Select(Items[next]);
        return Items[next];
    }
    public void Move(string id, int index)
    {
        if (index < 0 || index >= Items.Count) throw new InvalidOperationException("Queue index is invalid.");
        var entry = Items.Find(x => x.EntryId == id) ?? throw new InvalidOperationException("Queue entry was removed.");
        Items.Remove(entry); Items.Insert(index, entry);
    }
    public void Remove(string id)
    {
        if (Items.RemoveAll(x => x.EntryId == id) == 0) throw new InvalidOperationException("Queue entry was removed.");
        if (CurrentEntryId == id) Detach();
    }
    public void Clear() { Items.Clear(); Detach(); }
    public void Disconnect() { pendingVideo = null; ResetObservation(); }
    public void CancelSelection() => Detach();
    private void Detach() { CurrentEntryId = null; pendingVideo = null; ResetObservation(); }
    private void ResetObservation() { wasPlaying = false; observedSession = null; observedVideo = null; }
    // Only an observed, non-ended playback followed by ended in the same active session advances.
    public QueueEntry? Observe(string session, string? videoId, bool ended, string title)
    {
        var entry = Items.Find(x => x.EntryId == CurrentEntryId);
        if (entry == null) return null;
        if (pendingVideo != null)
        {
            if (videoId != pendingVideo) return null;
            pendingVideo = null;
        }
        if (entry.VideoId != videoId) { Detach(); return null; }
        var index = Items.IndexOf(entry);
        if (!string.IsNullOrWhiteSpace(title)) Items[index] = entry with { Title = title[..Math.Min(title.Length, 512)] };
        if (session != observedSession || videoId != observedVideo)
        {
            observedSession = session; observedVideo = videoId; wasPlaying = !ended;
            return null;
        }
        if (ended && wasPlaying)
        {
            wasPlaying = false;
            return Step(1);
        }
        if (!ended) wasPlaying = true;
        return null;
    }
}
