namespace Turnly.Core.Notifications;

/// <summary>The wording of every notification the server composes, in each language a user can pick.
/// Push and inbox text is rendered per recipient with <see cref="For"/> on their
/// <see cref="Turnly.Core.Entities.User.Language"/>, so one fan-out can reach people in different
/// languages. Titles are chore names (admin-entered) and stay as they are.</summary>
public abstract class NotificationText
{
    /// <summary>Language codes a user can store; the same codes the web client uses.</summary>
    public static readonly IReadOnlyList<string> Languages = ["en", "hr"];

    public static readonly NotificationText English = new EnglishText();
    public static readonly NotificationText Croatian = new CroatianText();

    /// <summary>The text for a stored language code. Null or unknown falls back to English.</summary>
    public static NotificationText For(string? language) => language == "hr" ? Croatian : English;

    public abstract string TestPush();

    /// <summary>"Ana", "Ana & Ben", "Ana, Ben & Cy".</summary>
    public abstract string People(IReadOnlyList<string> names);

    /// <summary>Someone else completed a chore the recipient holds.</summary>
    public abstract string Covered(string people);

    public abstract string OfferOpen(string from, int bounty);
    public abstract string OfferClaimed(string by, int bounty);
    public abstract string Nudge(string from, string? note);

    /// <summary>Scheduled entries. <paramref name="who"/> is the holder ("Ana" or "Ana & Ben"), or null.</summary>
    public abstract string DueNow(string? who, int points);
    public abstract string DueIn(string? who, TimeSpan span, int points);
    public abstract string Overdue(string? who, TimeSpan span, int points);

    /// <summary>Coarse duration bucket: minutes under an hour, then hours, then days. Null under a minute.</summary>
    protected static (int Count, char Unit)? Bucket(TimeSpan span)
    {
        var total = span < TimeSpan.Zero ? TimeSpan.Zero : span;
        if (total.TotalMinutes < 1)
            return null;
        if (total.TotalMinutes < 60)
            return ((int)Math.Round(total.TotalMinutes), 'm');
        if (total.TotalHours < 24)
            return ((int)Math.Round(total.TotalHours), 'h');
        return ((int)Math.Round(total.TotalDays), 'd');
    }

    private sealed class EnglishText : NotificationText
    {
        public override string TestPush() => "Test notification: push is working. 🎉";

        public override string People(IReadOnlyList<string> names) =>
            names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} & {names[^1]}";

        public override string Covered(string people) => $"{people} completed this chore for you.";

        public override string OfferOpen(string from, int bounty) =>
            $"{from} put this chore up for grabs{(bounty > 0 ? $" for {bounty} points" : "")}. Claim it before someone else does.";

        public override string OfferClaimed(string by, int bounty) =>
            $"{by} claimed this chore{(bounty > 0 ? $" and took your {bounty} points" : "")}. It's off your plate.";

        public override string Nudge(string from, string? note) =>
            note is null ? $"{from} nudged you about this chore." : $"{from} nudged you: {note}";

        public override string DueNow(string? who, int points) => $"{Prefix(who)}due now{Points(points)}";

        public override string DueIn(string? who, TimeSpan span, int points) =>
            $"{Prefix(who)}due in {Duration(span)}{Points(points)}";

        public override string Overdue(string? who, TimeSpan span, int points) =>
            $"{Prefix(who)}overdue by {Duration(span)}{Points(points)}";

        private static string Prefix(string? who) => who is null ? "" : $"{who} - ";
        private static string Points(int points) => points > 0 ? $" ({points} pts)" : "";

        private static string Duration(TimeSpan span)
        {
            if (Bucket(span) is not { } bucket)
                return "less than a minute";
            var (n, unit) = bucket;
            var word = unit switch { 'm' => "minute", 'h' => "hour", _ => "day" };
            return $"{n} {word}{(n == 1 ? "" : "s")}";
        }
    }

    /// <summary>Formal "vi", and no constructions that need a gendered past participle or a declined
    /// name (present tense, impersonal "Obavljeno"), matching the client's Croatian copy.</summary>
    private sealed class CroatianText : NotificationText
    {
        public override string TestPush() => "Probna obavijest: push radi. 🎉";

        public override string People(IReadOnlyList<string> names) =>
            names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} i {names[^1]}";

        public override string Covered(string people) => $"Obavljeno umjesto vas: {people}.";

        public override string OfferOpen(string from, int bounty) =>
            $"{from} nudi ovaj posao svima{(bounty > 0 ? $" za {bounty} {Form(bounty, "bod", "boda", "bodova")}" : "")}. Preuzmite ga prije drugih.";

        public override string OfferClaimed(string by, int bounty) =>
            $"{by} preuzima ovaj posao{(bounty > 0 ? $" i {Form(bounty, "vaš", "vaša", "vaših")} {bounty} {Form(bounty, "bod", "boda", "bodova")}" : "")}. Više nije na vama.";

        public override string Nudge(string from, string? note) =>
            note is null ? $"{from} vas podsjeća na ovaj posao." : $"{from} vas podsjeća: {note}";

        public override string DueNow(string? who, int points) => $"{Prefix(who)}rok je sada{Points(points)}";

        public override string DueIn(string? who, TimeSpan span, int points) =>
            $"{Prefix(who)}rok za {Duration(span)}{Points(points)}";

        public override string Overdue(string? who, TimeSpan span, int points) =>
            $"{Prefix(who)}kasni {Duration(span)}{Points(points)}";

        private static string Prefix(string? who) => who is null ? "" : $"{who} - ";
        private static string Points(int points) => points > 0 ? $" ({points} bod.)" : "";

        /// <summary>Accusative, as both "za ..." and "kasni ..." take it: 1 minutu, 2 minute, 5 minuta.</summary>
        private static string Duration(TimeSpan span)
        {
            if (Bucket(span) is not { } bucket)
                return "manje od minute";
            var (n, unit) = bucket;
            var word = unit switch
            {
                'm' => Form(n, "minutu", "minute", "minuta"),
                'h' => Form(n, "sat", "sata", "sati"),
                _ => Form(n, "dan", "dana", "dana"),
            };
            return $"{n} {word}";
        }

        /// <summary>Croatian plural forms: one (1, 21, 31...), few (2-4, 22-24...), other.</summary>
        private static string Form(int n, string one, string few, string other)
        {
            int mod10 = n % 10, mod100 = n % 100;
            if (mod10 == 1 && mod100 != 11) return one;
            if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14) return few;
            return other;
        }
    }
}
