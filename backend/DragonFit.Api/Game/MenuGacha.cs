using System.Globalization;
using System.Text;
using DragonFit.Api.Domain;

namespace DragonFit.Api.Game;

public record GachaPick(Food Food, decimal Servings, string Slot, string Rarity);
public record GachaResult(IReadOnlyList<GachaPick> Items, int Kcal, int ProteinG, int PriceVnd);

/// <summary>
/// The daily menu gacha (plan B3), done by an algorithm instead of the AI so it costs nothing:
/// many random menus are drawn and the one closest to the kcal target (±10%) with enough protein wins.
/// Rarity comes from protein per 100 kcal, so the "lucky" pulls are the high-protein dishes.
/// </summary>
public static class MenuGacha
{
    static readonly string[] Excluded = ["sugary", "alcohol", "supplement", "fried"];

    public static string Rarity(Food f)
    {
        if (f.Kcal <= 0) return "Common";
        var perHundred = f.ProteinG / f.Kcal * 100;
        return perHundred >= 10 ? "Epic" : perHundred >= 6 ? "Rare" : "Common";
    }

    /// <summary>Lower-case, no Vietnamese accents: "Thịt bò" and "thit bo" both match.</summary>
    public static string Fold(string s)
    {
        var d = s.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    public static bool Disliked(Food f, IReadOnlyCollection<string> dislikes) =>
        dislikes.Select(Fold).Where(d => d.Length >= 2).Any(d => Fold(f.NameVi).Contains(d) || f.Tags.Contains(d));

    public static GachaResult Draw(IReadOnlyCollection<Food> foods, int targetKcal, int targetProtein,
        IReadOnlyCollection<string> dislikes, int? budgetVnd, Random rng, int attempts = 400)
    {
        var ok = foods.Where(f => f.IsActive && !f.Tags.Any(Excluded.Contains) && !Disliked(f, dislikes)).ToList();
        List<Food> Pool(params string[] tags) => ok.Where(f => f.Tags.Any(tags.Contains)).ToList();

        var breakfast = Pool("breakfast");
        var meals = Pool("meal");
        var protein = ok.Where(f => f.Tags.Contains("protein") && f.Tags.Contains("home")).ToList();
        var veg = Pool("veg");
        var snacks = Pool("fruit", "dairy", "nuts");
        var rice = ok.FirstOrDefault(f => f.Code == "com_trang");

        GachaResult? best = null;
        var bestScore = double.MaxValue;
        for (var i = 0; i < attempts; i++)
        {
            var picks = new List<GachaPick>();
            void Add(List<Food> pool, string slot, params decimal[] servings)
            {
                if (pool.Count == 0) return;
                var f = pool[rng.Next(pool.Count)];
                if (picks.Any(p => p.Food.Id == f.Id && p.Slot == slot)) return;
                picks.Add(new GachaPick(f, servings[rng.Next(servings.Length)], slot, Rarity(f)));
            }

            Add(breakfast, "breakfast", 1);
            if (rng.Next(2) == 0) Add(protein.Where(f => f.Tags.Contains("breakfast")).ToList(), "breakfast", 1, 2);
            // Lunch: a street/canteen dish. Dinner: home style rice + protein + vegetables.
            Add(meals, "lunch", 1);
            if (rice is not null) picks.Add(new GachaPick(rice, rng.Next(2) == 0 ? 1 : 1.5m, "dinner", Rarity(rice)));
            Add(protein, "dinner", 1, 1.5m, 2);
            Add(veg, "dinner", 1);
            if (rng.Next(2) == 0) Add(snacks, "snack", 1);

            var kcal = picks.Sum(p => p.Food.Kcal * p.Servings);
            var prot = picks.Sum(p => p.Food.ProteinG * p.Servings);
            var price = picks.Sum(p => p.Food.PriceVnd * p.Servings);

            var score = Math.Abs((double)kcal - targetKcal) / targetKcal * 100          // % off the kcal target
                        + Math.Max(0, targetProtein - (double)prot) / targetProtein * 60 // missing protein
                        + (budgetVnd is > 0 && price > budgetVnd ? (double)(price - budgetVnd.Value) / budgetVnd.Value * 40 : 0)
                        + rng.NextDouble() * 2;                                          // a little variety between equal menus
            if (score < bestScore)
            {
                bestScore = score;
                best = new GachaResult(picks, (int)Math.Round(kcal), (int)Math.Round(prot), (int)Math.Round(price));
            }
        }
        return best ?? new GachaResult([], 0, 0, 0);
    }
}
