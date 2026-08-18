namespace Poe2DeskTracker.Currency;

/// <summary>
/// Names the fixed, dedicated cells of the standard PoE 2 currency tab.
/// This is deliberately position based: the frame exists even when a currency
/// is absent, so icon recognition would only make this less reliable.
/// </summary>
internal static class CurrencyTabProfile
{
    private static readonly IReadOnlyDictionary<string, string> PoeNinjaNamesByRussianName =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Сфера превращения"] = "Orb of Transmutation",
            ["Большая сфера превращения"] = "Greater Orb of Transmutation",
            ["Совершенная сфера превращения"] = "Perfect Orb of Transmutation",
            ["Сфера алхимии"] = "Orb of Alchemy",
            ["Сфера ваал"] = "Vaal Orb",
            ["Сфера отмены"] = "Orb of Annulment",
            ["Малая сфера златокузнеца"] = "Lesser Jeweller's Orb",
            ["Большая сфера златокузнеца"] = "Greater Jeweller's Orb",
            ["Совершенная сфера златокузнеца"] = "Perfect Jeweller's Orb",
            ["Сфера усиления"] = "Orb of Augmentation",
            ["Большая сфера усиления"] = "Greater Orb of Augmentation",
            ["Совершенная сфера усиления"] = "Perfect Orb of Augmentation",
            ["Сфера удачи"] = "Orb of Chance",
            ["Раскалывающая сфера"] = "Fracturing Orb",
            ["Божественная сфера"] = "Divine Orb",
            ["Сфера астромантии"] = "Artificer's Orb",
            ["Сфера царей"] = "Regal Orb",
            ["Большая сфера царей"] = "Greater Regal Orb",
            ["Совершенная сфера царей"] = "Perfect Regal Orb",
            ["Зеркало Каландры"] = "Mirror of Kalandra",
            ["Прядь Хинекоры"] = "Hinekora's Lock",
            ["Резец чародея"] = "Arcanist's Etcher",
            ["Деталь доспеха"] = "Armourer's Scrap",
            ["Точильный камень"] = "Blacksmith's Whetstone",
            ["Сфера возвышения"] = "Exalted Orb",
            ["Большая сфера возвышения"] = "Greater Exalted Orb",
            ["Совершенная сфера возвышения"] = "Perfect Exalted Orb",
            ["Стекольная масса"] = "Glassblower's Bauble",
            ["Призма камнереза"] = "Gemcutter's Prism",
            ["Сфера хаоса"] = "Chaos Orb",
            ["Большая сфера хаоса"] = "Greater Chaos Orb",
            ["Совершенная сфера хаоса"] = "Perfect Chaos Orb",
            ["Свиток мудрости"] = "Scroll of Wisdom",
        };

    private static readonly string[][] NamesByRow =
    [
        [
            "Сфера превращения",
            "Большая сфера превращения",
            "Совершенная сфера превращения",
            "Сфера алхимии",
            "Сфера ваал",
            "Сфера отмены",
            "Малая сфера златокузнеца",
            "Большая сфера златокузнеца",
            "Совершенная сфера златокузнеца",
        ],
        [
            "Сфера усиления",
            "Большая сфера усиления",
            "Совершенная сфера усиления",
            "Сфера удачи",
            "Раскалывающая сфера",
            "Божественная сфера",
            "Сфера астромантии",
        ],
        [
            "Сфера царей",
            "Большая сфера царей",
            "Совершенная сфера царей",
            "Зеркало Каландры",
            "Прядь Хинекоры",
        ],
        [
            "Резец чародея",
            "Деталь доспеха",
            "Точильный камень",
        ],
        [
            "Сфера возвышения",
            "Большая сфера возвышения",
            "Совершенная сфера возвышения",
        ],
        [
            "Стекольная масса",
            "Призма камнереза",
        ],
        [
            "Сфера хаоса",
            "Большая сфера хаоса",
            "Совершенная сфера хаоса",
        ],
        ["Свиток мудрости"],
    ];

    // Builds prior to Russian localization wrote these automatically. Treat them
    // as defaults on the next calibration, rather than as a user's custom name.
    private static readonly HashSet<string> LegacyEnglishNames =
    [
        "Orb of Transmutation", "Greater Orb of Transmutation", "Perfect Orb of Transmutation",
        "Orb of Alchemy", "Vaal Orb", "Orb of Annulment",
        "Lesser Jeweller's Orb", "Greater Jeweller's Orb", "Perfect Jeweller's Orb",
        "Orb of Augmentation", "Greater Orb of Augmentation", "Perfect Orb of Augmentation",
        "Orb of Chance", "Mirror of Kalandra", "Divine Orb", "Artificer's Orb",
        "Exalted Orb", "Greater Exalted Orb", "Perfect Exalted Orb",
        "Fracturing Orb", "Hinekora's Lock", "Arcanist's Etcher", "Armourer's Scrap",
        "Blacksmith's Whetstone", "Regal Orb", "Greater Regal Orb", "Perfect Regal Orb",
        "Glassblower's Bauble", "Gemcutter's Prism", "Chaos Orb", "Greater Chaos Orb",
        "Perfect Chaos Orb", "Scroll of Wisdom",
    ];

    internal static IReadOnlyList<DetectedCurrencySlot> Apply(IReadOnlyList<DetectedCurrencySlot> slots)
    {
        if (slots.Count == 0)
        {
            return slots;
        }

        var rows = GroupIntoRows(slots);
        if (!MatchesStandardRows(rows))
        {
            return slots;
        }

        var namedSlots = new List<DetectedCurrencySlot>(slots.Count);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Count; columnIndex++)
            {
                var slot = rows[rowIndex][columnIndex];
                namedSlots.Add(slot with { Name = NamesByRow[rowIndex][columnIndex] });
            }
        }

        return namedSlots
            .OrderBy(slot => slot.Bounds.Top)
            .ThenBy(slot => slot.Bounds.Left)
            .ToArray();
    }

    /// <summary>
    /// Returns true only for the dedicated PoE 2 Currency-tab geometry. A mere
    /// collection of gold slot borders is not enough: regular stash grids and
    /// decorative game scenery must never be sent to currency OCR.
    /// </summary>
    internal static bool MatchesStandardLayout(IReadOnlyList<DetectedCurrencySlot> slots) =>
        slots.Count > 0 && MatchesStandardRows(GroupIntoRows(slots));

    /// <summary>
    /// Verifies both the unique standard row pattern and its normalized
    /// position against the user's saved calibration. This prevents a
    /// coincidental 33-frame pattern elsewhere on screen from being accepted.
    /// </summary>
    internal static bool MatchesCalibratedLayout(
        IReadOnlyList<DetectedCurrencySlot> slots,
        CurrencyLayout layout,
        int imageWidth,
        int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 ||
            layout.Slots.Count != slots.Count ||
            !MatchesStandardLayout(slots))
        {
            return false;
        }

        var detected = slots
            .OrderBy(slot => slot.Bounds.Top)
            .ThenBy(slot => slot.Bounds.Left)
            .ToArray();
        var calibrated = layout.Slots
            .OrderBy(slot => slot.Y)
            .ThenBy(slot => slot.X)
            .ToArray();

        return calibrated.Zip(detected).All(pair => MatchesCalibratedSlot(
            pair.First,
            pair.Second,
            imageWidth,
            imageHeight));
    }

    internal static bool IsAutomaticName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (LegacyEnglishNames.Contains(name) || NamesByRow.SelectMany(row => row).Contains(name, StringComparer.Ordinal));

    /// <summary>
    /// Converts the localized fixed-tab label to the English item name used by
    /// the price feed. Legacy layouts already use that English name directly.
    /// </summary>
    internal static bool TryGetPoeNinjaName(string name, out string poeNinjaName)
    {
        if (PoeNinjaNamesByRussianName.TryGetValue(name, out poeNinjaName!))
        {
            return true;
        }

        if (LegacyEnglishNames.Contains(name))
        {
            poeNinjaName = name;
            return true;
        }

        poeNinjaName = string.Empty;
        return false;
    }

    private static bool MatchesStandardRows(IReadOnlyList<List<DetectedCurrencySlot>> rows) =>
        rows.Count == NamesByRow.Length &&
        rows.Select(row => row.Count).SequenceEqual(NamesByRow.Select(row => row.Length));

    private static bool MatchesCalibratedSlot(
        CurrencySlotDefinition calibrated,
        DetectedCurrencySlot detected,
        int imageWidth,
        int imageHeight)
    {
        var detectedX = (double)detected.Bounds.Left / imageWidth;
        var detectedY = (double)detected.Bounds.Top / imageHeight;
        var detectedWidth = (double)detected.Bounds.Width / imageWidth;
        var detectedHeight = (double)detected.Bounds.Height / imageHeight;
        var centerDeltaX = Math.Abs(
            calibrated.X + calibrated.Width / 2 -
            (detectedX + detectedWidth / 2));
        var centerDeltaY = Math.Abs(
            calibrated.Y + calibrated.Height / 2 -
            (detectedY + detectedHeight / 2));
        var maximumCenterDeltaX = Math.Max(0.012, calibrated.Width * 0.45);
        var maximumCenterDeltaY = Math.Max(0.012, calibrated.Height * 0.45);
        var maximumWidthDelta = Math.Max(0.01, calibrated.Width * 0.4);
        var maximumHeightDelta = Math.Max(0.01, calibrated.Height * 0.4);

        return centerDeltaX <= maximumCenterDeltaX &&
               centerDeltaY <= maximumCenterDeltaY &&
               Math.Abs(calibrated.Width - detectedWidth) <= maximumWidthDelta &&
               Math.Abs(calibrated.Height - detectedHeight) <= maximumHeightDelta;
    }

    private static List<List<DetectedCurrencySlot>> GroupIntoRows(IReadOnlyList<DetectedCurrencySlot> slots)
    {
        // The tab deliberately offsets the row holding the three quality items by
        // less than one frame height. Keep this below that offset, while allowing
        // several pixels of imperfect border detection inside a real row.
        var rowTolerance = Math.Max(6, (int)Math.Round(slots.Select(slot => slot.Bounds.Height).Order().ElementAt(slots.Count / 2) * 0.18));
        var rows = new List<List<DetectedCurrencySlot>>();

        foreach (var slot in slots.OrderBy(slot => slot.Bounds.Top).ThenBy(slot => slot.Bounds.Left))
        {
            var row = rows.LastOrDefault(existing => Math.Abs(existing[0].Bounds.Top - slot.Bounds.Top) <= rowTolerance);
            if (row is null)
            {
                row = [];
                rows.Add(row);
            }

            row.Add(slot);
        }

        foreach (var row in rows)
        {
            row.Sort((left, right) => left.Bounds.Left.CompareTo(right.Bounds.Left));
        }

        return rows;
    }
}
