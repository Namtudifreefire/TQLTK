using System.Security.Cryptography;

namespace PersonalInfoManager.Services;

public sealed record PasswordGeneratorOptions(
    int Length,
    bool Uppercase,
    bool Lowercase,
    bool Digits,
    bool Symbols,
    bool ExcludeAmbiguous,
    bool UsePassphrase,
    int WordCount,
    string Separator);

public static class PasswordGeneratorService
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Numeric = "0123456789";
    private const string Punctuation = "!@#$%^&*_-+=?";
    private static readonly string[] Words = ("acorn actor amber anchor apple apron arcade arrow artist atlas autumn " +
        "baker bamboo beacon berry bicycle biscuit blanket blossom bluebird border bottle breeze bridge" +
        " " +
        "cabin cactus camera candle canyon carpet castle cedar celery cherry circle citrus cloud coffee" +
        " " +
        "comet compass copper coral cricket crystal cupcake daisy dancer daylight denim desert diamond" +
        " " +
        "dinner dolphin domino dragon dream drift eagle earth echo elbow emerald engine evening family" +
        " " +
        "feather festival field figure firefly flag flower flute forest fossil fountain fox friend galaxy" +
        " " +
        "garden garlic gazelle gentle ginger glacier glider gold goose granite grape green guitar harbor" +
        " " +
        "harmony hazel helmet honey horizon hotel hummingbird island ivory jacket jasmine jelly jewel" +
        " " +
        "journey kettle kiwi lantern lavender lemon library lilac linen lion little lizard maple marble" +
        " " +
        "market meadow melody memory mitten mocha mountain movie muffin museum music mustard nature" +
        " " +
        "nectar noodle north notebook ocean olive onion orange orchid origami otter owl painter palace" +
        " " +
        "panda paper paprika parade parcel peach peanut pebble pepper picnic pillow planet plum pocket" +
        " " +
        "pocket poetry polar popcorn prairie prism pumpkin purple puzzle quartz rabbit radio rainbow" +
        " " +
        "raven recipe redwood ribbon river robin rocket rose saddle safari sage sailor salmon sandal" +
        " " +
        "sapphire scarf season shadow shark shelter silver singer sketch skillet snowflake solar spirit" +
        " " +
        "sprout squirrel stadium starfish station stone sugar sunset sweater swift table teacher thimble" +
        " " +
        "thunder ticket timber tomato topaz tourist tulip tunnel turtle umbrella uncle uniform universe" +
        " " +
        "valley velvet village violet violin visitor walnut waterfall weather whale willow window winter" +
        " " +
        "wonder xylophone yacht yellow yogurt zebra zephyr zipper almond arch balcony basket battery" +
        " " +
        "bloom bonfire bracelet branch button butterfly calendar canyon caraway carnival cardboard" +
        " " +
        "careful carpeted cheerful cinnamon clay clover coast confident cookie courtyard cradle" +
        " " +
        "crown crystal clear curious daffodil dainty dandelion daring dawn dewdrop digital discovery" +
        " " +
        "doodle double downtown eager easy evergreen explorer fearless fern flamingo footprint fortune" +
        " " +
        "friday friendly frosty fruitful gadget glitter golden graceful hammock happy helpful hidden" +
        " " +
        "hickory hillside humble imagine ink joyful jungle kitten lagoon lemon lighthearted lighthouse" +
        " " +
        "lively lunar magnet majestic mindful moonlight morning napkin nearby newt nimble novelties" +
        " " +
        "oak object oatmeal observant opal orangeade orchard peaceful penguin peppercorn perfect petal" +
        " " +
        "playful pleasant plumage popcorn promise quick quiet rambler red ribbon rosemary round" +
        " " +
        "saffron sailboat sandstone secret shimmer shoreline sincere sleepy smooth snowstorm soft" +
        " " +
        "sparkle splendid springtime stable sunshine thoughtful tidy twilight useful vacation vibrant" +
        " " +
        "warmhearted welcoming westward whistling wholehearted wildflower windmill wishful woodland" +
        " " +
        "wonderful youthful zenith zestful zigzag zucchini")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public static string Generate(PasswordGeneratorOptions options)
        => Generate(options, null);

    public static string Generate(PasswordGeneratorOptions options, WebsitePasswordPolicy? websitePolicy)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.UsePassphrase)
        {
            if (websitePolicy is { Enabled: true })
                throw new InvalidOperationException("Không thể bảo đảm quy tắc ký tự của website bằng cụm mật khẩu. Hãy chọn chế độ mật khẩu ngẫu nhiên.");
            if (options.WordCount is < 6 or > 12)
                throw new ArgumentOutOfRangeException(nameof(options.WordCount), "Số từ phải từ 6 đến 12.");
            string separator = options.Separator is " " or "-" or "." or "_" ? options.Separator : "-";
            var selected = new string[options.WordCount];
            for (int i = 0; i < selected.Length; i++)
                selected[i] = Words[RandomNumberGenerator.GetInt32(Words.Length)];
            return string.Join(separator, selected);
        }

        if (options.Length is < 8 or > 64)
            throw new ArgumentOutOfRangeException(nameof(options.Length), "Độ dài phải từ 8 đến 64 ký tự.");

        if (websitePolicy is { Enabled: true } &&
            WebsitePasswordPolicyService.Validate(options.Length, options, websitePolicy) is string policyError)
            throw new InvalidOperationException(policyError);

        var sets = new List<string>();
        if (options.Uppercase || websitePolicy is { Enabled: true, RequireUppercase: true }) sets.Add(Upper);
        if (options.Lowercase || websitePolicy is { Enabled: true, RequireLowercase: true }) sets.Add(Lower);
        if (options.Digits || websitePolicy is { Enabled: true, RequireDigits: true }) sets.Add(Numeric);
        if (options.Symbols || websitePolicy is { Enabled: true, RequireSymbols: true }) sets.Add(Punctuation);
        if (sets.Count == 0)
            throw new InvalidOperationException("Chọn ít nhất một nhóm ký tự.");
        if (options.Length < sets.Count)
            throw new InvalidOperationException("Độ dài phải đủ để có ít nhất một ký tự trong mỗi nhóm đã chọn.");

        const string ambiguous = "0O1Il|`'\"";
        if (options.ExcludeAmbiguous)
            sets = sets.Select(set => new string(set.Where(c => !ambiguous.Contains(c)).ToArray())).ToList();
        if (websitePolicy is { Enabled: true } && !string.IsNullOrEmpty(websitePolicy.ExcludedCharacters))
            sets = sets.Select(set => new string(set.Where(c => !websitePolicy.ExcludedCharacters.Contains(c)).ToArray())).ToList();
        if (sets.Any(string.IsNullOrEmpty))
            throw new InvalidOperationException("Không còn ký tự hợp lệ trong một nhóm đã chọn.");

        string all = string.Concat(sets);
        var chars = new List<char>(options.Length);
        foreach (string set in sets) chars.Add(set[RandomNumberGenerator.GetInt32(set.Length)]);
        while (chars.Count < options.Length)
            chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);

        for (int i = chars.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }
}
