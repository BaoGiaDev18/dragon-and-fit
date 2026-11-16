using Microsoft.AspNetCore.Identity;

namespace DragonFit.Api.Domain;

public class AppUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ---------------------------------------------------------------- Player

public enum CharacterStatus { Alive, Dead }

public class Character
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; } = 1;
    public long TotalXp { get; set; }
    public int CurrentHp { get; set; } = 100;
    public CharacterStatus Status { get; set; } = CharacterStatus.Alive;
    public int EvolutionStage { get; set; } = 1;
    public int AvatarModelId { get; set; }
    public AvatarModel? AvatarModel { get; set; }
    public int? ActiveTitleId { get; set; }
    public Title? ActiveTitle { get; set; }
    public int StreakDays { get; set; }
    public DateOnly? LastStreakDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DiedAt { get; set; }
    public DateTime? PurgeAt { get; set; }
    public DateOnly? RestPassDate { get; set; }       // a rest pass covers this day (B2 shop)
    public string? CustomSpriteKey { get; set; }       // uploaded sprite sheet (B4)
    public DateOnly? RestModeFrom { get; set; }
    public DateOnly? RestModeUntil { get; set; }
}

public class PlayerProfile
{
    public string UserId { get; set; } = "";
    public string Sex { get; set; } = "";
    public int BirthYear { get; set; }
    public decimal HeightCm { get; set; }
    public string Goal { get; set; } = "recomposition";
    public string? GoalTarget { get; set; }
    public DateOnly? GoalDeadline { get; set; }
    public int? BaselineSteps { get; set; }
    public TimeOnly? WorkStart { get; set; }
    public TimeOnly? WorkEnd { get; set; }
    public int TrainingDaysPerWeek { get; set; } = 3;
    public string PreferredTrainingTime { get; set; } = "evening";
    public int SessionMinutes { get; set; } = 45;
    public TimeOnly QuestTime { get; set; } = new(6, 0);
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public List<string> Equipment { get; set; } = [];
    public List<string> ExcludedBodyParts { get; set; } = [];
    public TimeOnly? Bedtime { get; set; }
    public TimeOnly? WakeTime { get; set; }
    public string? MealsPerDay { get; set; }
    public List<string> EatingPlaces { get; set; } = [];
    public List<string> Dislikes { get; set; } = [];
    public int? FoodBudget { get; set; }
    public string? CanCook { get; set; }
    public int? MaintenanceKcal { get; set; }
    public int? DailyDeficit { get; set; }
}

public class BodyMetric
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public DateOnly Date { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? WaistCm { get; set; }
    public decimal? BodyFatPct { get; set; }
    public decimal? SleepHours { get; set; }
    public int? EnergyScore { get; set; }
}

public class StatSnapshot
{
    public long Id { get; set; }
    public Guid CharacterId { get; set; }
    public DateOnly Date { get; set; }
    public int Str { get; set; }
    public int Vit { get; set; }
    public int End { get; set; }
    public int Dis { get; set; }
    public int Wis { get; set; }
}

public class Title
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "Level"; // Level / Achievement / Boss
    public int? MinLevel { get; set; }
}

public class CharacterTitle
{
    public Guid CharacterId { get; set; }
    public int TitleId { get; set; }
    public Title? Title { get; set; }
    public DateTime UnlockedAt { get; set; } = DateTime.UtcNow;
}

// ---------------------------------------------------------------- Avatars

public class AvatarModel
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string BodyType { get; set; } = "";
    public string SpriteSheetUrl { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public List<AvatarEvolution> Evolutions { get; set; } = [];
}

public class AvatarEvolution
{
    public int Id { get; set; }
    public int AvatarModelId { get; set; }
    public int Stage { get; set; }
    public int MinLevel { get; set; }
    public int RowIndex { get; set; }
}

// ---------------------------------------------------------------- Onboarding

public class OnboardingStep
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int SortOrder { get; set; }
    public string Title { get; set; } = "";
    public string Narration { get; set; } = "";
    public bool IsSkippable { get; set; }
    public bool IsActive { get; set; } = true;
    public List<OnboardingQuestion> Questions { get; set; } = [];
}

public class OnboardingQuestion
{
    public int Id { get; set; }
    public int StepId { get; set; }
    public string Code { get; set; } = "";
    public int SortOrder { get; set; }
    public string Prompt { get; set; } = "";
    public string? HelpText { get; set; }
    public string InputType { get; set; } = "text";
    public string? Unit { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    /// <summary>Optional regular expression the text answer must match (e.g. character names: A–Z only).</summary>
    public string? Pattern { get; set; }
    public string? PatternMessage { get; set; }
    public bool IsRequired { get; set; }
    public bool IsSensitive { get; set; }
    public bool IsActive { get; set; } = true;
    public List<QuestionOption> Options { get; set; } = [];
}

public class QuestionOption
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public int SortOrder { get; set; }
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsExclusive { get; set; }
}

public class PlayerAnswer
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public int QuestionId { get; set; }
    public string ValueJson { get; set; } = "null";
    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;
}

public enum OnboardingStatus { NotStarted, InProgress, Completed }

public class OnboardingProgress
{
    public string UserId { get; set; } = "";
    public int? CurrentStepId { get; set; }
    public OnboardingStatus Status { get; set; } = OnboardingStatus.NotStarted;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

// ---------------------------------------------------------------- Training

public class Exercise
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameVi { get; set; } = "";
    public string Group { get; set; } = "";      // push / pull / legs / core / cardio / mobility
    public string BodyPart { get; set; } = "";   // used to exclude painful areas
    public string Equipment { get; set; } = "none";
    public int Tier { get; set; } = 1;
    public string Unit { get; set; } = "reps";   // reps / seconds
    public decimal KcalPerRep { get; set; }
    public string FormTips { get; set; } = "";
}

public enum SessionType { Normal, Baseline, Boss }

public class WorkoutSession
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public SessionType SessionType { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public Guid? QuestInstanceId { get; set; }
    public List<WorkoutSet> Sets { get; set; } = [];
}

public class WorkoutSet
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public int ExerciseId { get; set; }
    public Exercise? Exercise { get; set; }
    public int Reps { get; set; }
    public decimal KcalBurned { get; set; }
}

// ---------------------------------------------------------------- Quests

public enum QuestStatus { Pending, Completed, Partial }

public class QuestTemplate
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Type { get; set; } = "daily";  // daily / weekly / side / boss / main
    public string Kind { get; set; } = "";       // exercise / steps / protein / mobility
    public int BaseXp { get; set; }
    public int BaseGems { get; set; }
    public int BaseDamage { get; set; }
}

public class QuestInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public Guid CharacterId { get; set; }
    public int TemplateId { get; set; }
    public QuestTemplate? Template { get; set; }
    public DateOnly Date { get; set; }
    public int Slot { get; set; }
    public int? ExerciseId { get; set; }
    public Exercise? Exercise { get; set; }
    public int Sets { get; set; } = 1;
    public int TargetValue { get; set; }
    public string Unit { get; set; } = "reps";
    public string Title { get; set; } = "";
    public string Narration { get; set; } = "";
    public int Xp { get; set; }
    public int Gems { get; set; }
    public bool IsRequired { get; set; } = true;
    public string? BossKind { get; set; }             // null / weekly / monthly
    public QuestStatus Status { get; set; }
    public int? DoneValue { get; set; }
    public int AwardedXp { get; set; }
    public int AwardedGems { get; set; }
    public string? ProofUrl { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string GeneratedBy { get; set; } = "rules";
}

// ---------------------------------------------------------------- Economy

public class Wallet
{
    public string UserId { get; set; } = "";
    public int Gems { get; set; }
}

public class GemTransaction
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public int Amount { get; set; }
    public string Reason { get; set; } = "";
    public string? RefId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ShopItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }               // utility items, e.g. "rest_pass"
    public string? Description { get; set; }
    public string Kind { get; set; } = "personal"; // utility / cosmetic / personal
    public string? Size { get; set; }               // small / medium / large
    public int Price { get; set; }
    public string? OwnerUserId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Purchase
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public int ShopItemId { get; set; }
    public ShopItem? ShopItem { get; set; }
    public int Price { get; set; }
    public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
    public DateOnly? UsedForDate { get; set; }       // rest pass: the day it covered
}

// ---------------------------------------------------------------- Food (Phase 2)

public class Food
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string NameVi { get; set; } = "";
    public string Serving { get; set; } = "";   // human description, e.g. "1 tô"
    public decimal ServingG { get; set; }
    public decimal Kcal { get; set; }            // per serving, estimate
    public decimal ProteinG { get; set; }
    public decimal CarbG { get; set; }
    public decimal FatG { get; set; }
    public int PriceVnd { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool IsActive { get; set; } = true;
}

public class MealLog
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public DateOnly Date { get; set; }           // player's local day
    public string Slot { get; set; } = "lunch";  // breakfast / lunch / dinner / snack
    public int FoodId { get; set; }
    public Food? Food { get; set; }
    public decimal Servings { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ---------------------------------------------------------------- HP (B10)

public class DailySettlement
{
    public long Id { get; set; }
    public Guid CharacterId { get; set; }
    public DateOnly Date { get; set; }
    public int TargetKcal { get; set; }
    public int ActualKcal { get; set; }
    public int BurnedKcal { get; set; }
    public int ProteinG { get; set; }
    public int ProteinTargetG { get; set; }
    public int MealsLogged { get; set; }
    public decimal QuestCompletion { get; set; }
    public int Damage { get; set; }
    public int Heal { get; set; }
    public bool IsPerfect { get; set; }
    public int PerfectStreak { get; set; }
    public bool IsRestMode { get; set; }
    public int HpBefore { get; set; }
    public int HpAfter { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SeenAt { get; set; }
}

public class HpEvent
{
    public long Id { get; set; }
    public Guid CharacterId { get; set; }
    public DateOnly Date { get; set; }
    public int Amount { get; set; }               // negative = damage
    public string Reason { get; set; } = "";      // Overeat / Undereat / NoFoodLog / QuestMissed / QuestPartial / DailyCap / PerfectDay / Sleep
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ---------------------------------------------------------------- Menu gacha (B3)

public enum MealDrawStatus { Rolled, Confirmed }

public class MealDraw
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public DateOnly Date { get; set; }
    public int RollNo { get; set; }
    public int TargetKcal { get; set; }
    public int TargetProteinG { get; set; }
    public int CostGems { get; set; }
    public MealDrawStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<MealDrawItem> Items { get; set; } = [];
}

public class MealDrawItem
{
    public long Id { get; set; }
    public long MealDrawId { get; set; }
    public int FoodId { get; set; }
    public Food? Food { get; set; }
    public decimal Servings { get; set; } = 1;
    public string Slot { get; set; } = "lunch";
    public string Rarity { get; set; } = "Common";   // Common / Rare / Epic
    public long? MealLogId { get; set; }             // set once the item was eaten and logged
}
