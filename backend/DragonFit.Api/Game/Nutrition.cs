namespace DragonFit.Api.Game;

public record NutritionTargets(int BmrKcal, int MaintenanceKcal, int Deficit, int TargetKcal, int ProteinG);

/// <summary>
/// Daily energy and protein targets with the guardrails from B3:
/// deficit capped at 500 kcal and never below a conservative calorie floor.
/// </summary>
public static class Nutrition
{
    public static NutritionTargets Calculate(string sex, decimal weightKg, decimal heightCm, int age, int trainingDaysPerWeek, string goal)
    {
        // Mifflin–St Jeor
        var bmr = 10m * weightKg + 6.25m * heightCm - 5m * age + (sex == "female" ? -161m : 5m);
        var factor = trainingDaysPerWeek switch { >= 5 => 1.55m, 4 => 1.45m, _ => 1.375m };
        var maintenance = RoundTo(bmr * factor, 10);

        var deficit = goal switch
        {
            "fat_loss" => 500,
            "muscle_gain" => -250, // small surplus
            _ => 300,              // recomposition
        };
        var floor = Math.Max(RoundTo(bmr, 10), sex == "female" ? 1200 : 1500);
        var target = Math.Max(maintenance - deficit, floor);

        var protein = RoundTo(1.8m * weightKg, 5);
        return new NutritionTargets(RoundTo(bmr, 10), maintenance, maintenance - target, target, protein);
    }

    static int RoundTo(decimal value, int step) => (int)(Math.Round(value / step, MidpointRounding.AwayFromZero) * step);
}
