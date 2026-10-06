using System.Globalization;
using System.Text;

namespace Kreyora.Domain.Storefront;

/// <summary>
/// Built-in place names for matching what a customer types to a shop's delivery zones (M09-S04 Q5): Nepal's 77
/// districts in English, Devanagari and common Romanized spellings, and major cities mapped to their district.
/// Reviewed by the owner at the M09-S04 checkpoint; sellers' own zone names always take precedence.
/// </summary>
public static class NepalGazetteer
{
    /// <summary>A resolved place: a district, optionally narrowed to a municipality/city.</summary>
    public sealed record Place(string District, string? Municipality);

    // District, Devanagari name, extra Romanized aliases. Grouped by province.
    private static readonly (string District, string Devanagari, string[] Aliases)[] Districts =
    [
        // Koshi
        ("Bhojpur", "भोजपुर", []),
        ("Dhankuta", "धनकुटा", []),
        ("Ilam", "इलाम", ["illam"]),
        ("Jhapa", "झापा", []),
        ("Khotang", "खोटाङ", []),
        ("Morang", "मोरङ", []),
        ("Okhaldhunga", "ओखलढुङ्गा", ["okhaldunga"]),
        ("Panchthar", "पाँचथर", ["panchthar"]),
        ("Sankhuwasabha", "सङ्खुवासभा", ["sankhuwa sabha"]),
        ("Solukhumbu", "सोलुखुम्बु", ["solu", "khumbu"]),
        ("Sunsari", "सुनसरी", []),
        ("Taplejung", "ताप्लेजुङ", []),
        ("Terhathum", "तेह्रथुम", ["tehrathum"]),
        ("Udayapur", "उदयपुर", ["udaypur"]),
        // Madhesh
        ("Bara", "बारा", []),
        ("Dhanusha", "धनुषा", ["dhanusa"]),
        ("Mahottari", "महोत्तरी", []),
        ("Parsa", "पर्सा", []),
        ("Rautahat", "रौतहट", []),
        ("Saptari", "सप्तरी", []),
        ("Sarlahi", "सर्लाही", []),
        ("Siraha", "सिराहा", []),
        // Bagmati
        ("Bhaktapur", "भक्तपुर", []),
        ("Chitwan", "चितवन", ["chitawan"]),
        ("Dhading", "धादिङ", []),
        ("Dolakha", "दोलखा", []),
        ("Kathmandu", "काठमाडौं", ["ktm", "kathmandau", "kathmadu"]),
        ("Kavrepalanchok", "काभ्रेपलाञ्चोक", ["kavre", "kabhre", "kavrepalanchowk", "kabhrepalanchok"]),
        ("Lalitpur", "ललितपुर", []),
        ("Makwanpur", "मकवानपुर", ["makawanpur"]),
        ("Nuwakot", "नुवाकोट", []),
        ("Ramechhap", "रामेछाप", ["ramechap"]),
        ("Rasuwa", "रसुवा", []),
        ("Sindhuli", "सिन्धुली", []),
        ("Sindhupalchok", "सिन्धुपाल्चोक", ["sindhupalchowk"]),
        // Gandaki
        ("Baglung", "बागलुङ", []),
        ("Gorkha", "गोरखा", []),
        ("Kaski", "कास्की", []),
        ("Lamjung", "लमजुङ", []),
        ("Manang", "मनाङ", []),
        ("Mustang", "मुस्ताङ", []),
        ("Myagdi", "म्याग्दी", []),
        ("Nawalpur", "नवलपुर", ["nawalparasi east", "nawalparasi purva"]),
        ("Parbat", "पर्वत", []),
        ("Syangja", "स्याङ्जा", []),
        ("Tanahun", "तनहुँ", ["tanahu"]),
        // Lumbini
        ("Arghakhanchi", "अर्घाखाँची", []),
        ("Banke", "बाँके", []),
        ("Bardiya", "बर्दिया", ["bardia"]),
        ("Dang", "दाङ", []),
        ("Eastern Rukum", "रुकुम पूर्व", ["rukum east", "rukum purva"]),
        ("Gulmi", "गुल्मी", []),
        ("Kapilvastu", "कपिलवस्तु", ["kapilbastu"]),
        ("Parasi", "परासी", ["nawalparasi west", "nawalparasi paschim"]),
        ("Palpa", "पाल्पा", []),
        ("Pyuthan", "प्युठान", []),
        ("Rolpa", "रोल्पा", []),
        ("Rupandehi", "रुपन्देही", []),
        // Karnali
        ("Dailekh", "दैलेख", []),
        ("Dolpa", "डोल्पा", []),
        ("Humla", "हुम्ला", []),
        ("Jajarkot", "जाजरकोट", []),
        ("Jumla", "जुम्ला", []),
        ("Kalikot", "कालिकोट", []),
        ("Mugu", "मुगु", []),
        ("Salyan", "सल्यान", []),
        ("Surkhet", "सुर्खेत", []),
        ("Western Rukum", "रुकुम पश्चिम", ["rukum west", "rukum paschim"]),
        // Sudurpashchim
        ("Achham", "अछाम", ["accham"]),
        ("Baitadi", "बैतडी", []),
        ("Bajhang", "बझाङ", []),
        ("Bajura", "बाजुरा", []),
        ("Dadeldhura", "डडेलधुरा", []),
        ("Darchula", "दार्चुला", []),
        ("Doti", "डोटी", []),
        ("Kailali", "कैलाली", []),
        ("Kanchanpur", "कञ्चनपुर", [])
    ];

    // City or municipality → district. City names are kept as the municipality so municipality-level zones match.
    private static readonly (string City, string District, string[] Aliases)[] Cities =
    [
        ("Pokhara", "Kaski", ["पोखरा"]),
        ("Bharatpur", "Chitwan", ["भरतपुर", "narayangarh", "narayanghat"]),
        ("Biratnagar", "Morang", ["विराटनगर"]),
        ("Birgunj", "Parsa", ["वीरगञ्ज", "birganj"]),
        ("Butwal", "Rupandehi", ["बुटवल"]),
        ("Siddharthanagar", "Rupandehi", ["bhairahawa", "भैरहवा"]),
        ("Dharan", "Sunsari", ["धरान"]),
        ("Itahari", "Sunsari", ["इटहरी"]),
        ("Hetauda", "Makwanpur", ["हेटौंडा"]),
        ("Janakpur", "Dhanusha", ["janakpurdham", "जनकपुर"]),
        ("Nepalgunj", "Banke", ["नेपालगञ्ज", "nepalganj"]),
        ("Kohalpur", "Banke", []),
        ("Dhangadhi", "Kailali", ["धनगढी"]),
        ("Tikapur", "Kailali", []),
        ("Bhimdatta", "Kanchanpur", ["mahendranagar"]),
        ("Birendranagar", "Surkhet", []),
        ("Tulsipur", "Dang", []),
        ("Ghorahi", "Dang", []),
        ("Damak", "Jhapa", []),
        ("Birtamod", "Jhapa", ["birtamode"]),
        ("Mechinagar", "Jhapa", ["kakarbhitta", "kakarvitta"]),
        ("Banepa", "Kavrepalanchok", []),
        ("Dhulikhel", "Kavrepalanchok", []),
        ("Kirtipur", "Kathmandu", []),
        ("Lalitpur", "Lalitpur", ["patan", "पाटन"]),
        ("Tansen", "Palpa", []),
        ("Kalaiya", "Bara", []),
        ("Gaur", "Rautahat", []),
        ("Rajbiraj", "Saptari", []),
        ("Lahan", "Siraha", []),
        ("Malangwa", "Sarlahi", []),
        ("Jaleshwar", "Mahottari", []),
        ("Waling", "Syangja", []),
        ("Besisahar", "Lamjung", []),
        ("Damauli", "Tanahun", ["vyas"]),
        ("Kawasoti", "Nawalpur", []),
        ("Gaindakot", "Nawalpur", []),
        ("Gulariya", "Bardiya", [])
    ];

    // Areas that span several districts: the caller asks the customer which one.
    private static readonly (string Name, string[] Districts, string[] Aliases)[] Regions =
    [
        ("Kathmandu Valley", ["Kathmandu", "Lalitpur", "Bhaktapur"], ["valley", "upatyaka", "उपत्यका", "काठमाडौं उपत्यका", "ktm valley"])
    ];

    private static readonly HashSet<string> SuffixWords = new(StringComparer.Ordinal)
    {
        "district", "jilla", "jila", "जिल्ला", "metropolitan", "submetropolitan", "sub", "city", "municipality", "nagarpalika",
        "mahanagarpalika", "upamahanagarpalika", "महानगरपालिका", "उपमहानगरपालिका", "नगरपालिका", "महानगर", "उपमहानगर", "ma", "tira", "side"
    };

    private static readonly Dictionary<string, List<Place>> Index = BuildIndex();
    private static readonly Dictionary<string, string> DistrictByKey = BuildDistrictIndex();

    /// <summary>Comparison key: NFC, lowercase, punctuation removed, suffix words like "district" or "nagarpalika" dropped.</summary>
    public static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormC))
        {
            var category = char.GetUnicodeCategory(ch);
            builder.Append(char.IsLetterOrDigit(ch) || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                ? char.ToLowerInvariant(ch)
                : ' ');
        }

        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = words.Where(word => !SuffixWords.Contains(word)).ToArray();
        return string.Join(' ', kept.Length > 0 ? kept : words);
    }

    /// <summary>Places a name can mean: none (unknown), one, or several (a region spanning districts).</summary>
    public static IReadOnlyList<Place> Lookup(string? text)
    {
        var key = Key(text);
        return key.Length > 0 && Index.TryGetValue(key, out var places) ? places : [];
    }

    /// <summary>The canonical district for a district name in any known spelling, or null.</summary>
    public static string? CanonicalDistrict(string? text)
    {
        var key = Key(text);
        return key.Length > 0 && DistrictByKey.TryGetValue(key, out var district) ? district : null;
    }

    /// <summary>All 77 districts (for tests and review).</summary>
    public static IReadOnlyList<string> AllDistricts => [.. Districts.Select(d => d.District)];

    private static Dictionary<string, string> BuildDistrictIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (district, devanagari, aliases) in Districts)
        {
            foreach (var name in aliases.Append(district).Append(devanagari))
            {
                index[Key(name)] = district;
            }
        }

        return index;
    }

    private static Dictionary<string, List<Place>> BuildIndex()
    {
        var index = new Dictionary<string, List<Place>>(StringComparer.Ordinal);
        void Add(string name, Place place)
        {
            var key = Key(name);
            if (!index.TryGetValue(key, out var list)) index[key] = list = [];
            if (!list.Contains(place)) list.Add(place);
        }

        foreach (var (district, devanagari, aliases) in Districts)
        {
            foreach (var name in aliases.Append(district).Append(devanagari)) Add(name, new Place(district, null));
        }

        foreach (var (city, district, aliases) in Cities)
        {
            foreach (var name in aliases.Append(city))
            {
                // A city named like its district (e.g. Kathmandu) stays a district-level match.
                var key = Key(name);
                if (index.ContainsKey(key)) continue;
                Add(name, new Place(district, city));
            }
        }

        foreach (var (region, districts, aliases) in Regions)
        {
            foreach (var name in aliases.Append(region))
            {
                foreach (var district in districts) Add(name, new Place(district, null));
            }
        }

        return index;
    }
}
