using DragonFit.Api.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<PlayerProfile> PlayerProfiles => Set<PlayerProfile>();
    public DbSet<BodyMetric> BodyMetrics => Set<BodyMetric>();
    public DbSet<StatSnapshot> StatSnapshots => Set<StatSnapshot>();
    public DbSet<Title> Titles => Set<Title>();
    public DbSet<CharacterTitle> CharacterTitles => Set<CharacterTitle>();
    public DbSet<AvatarModel> AvatarModels => Set<AvatarModel>();
    public DbSet<AvatarEvolution> AvatarEvolutions => Set<AvatarEvolution>();
    public DbSet<OnboardingStep> OnboardingSteps => Set<OnboardingStep>();
    public DbSet<OnboardingQuestion> OnboardingQuestions => Set<OnboardingQuestion>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<PlayerAnswer> PlayerAnswers => Set<PlayerAnswer>();
    public DbSet<OnboardingProgress> OnboardingProgress => Set<OnboardingProgress>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<WorkoutSession> WorkoutSessions => Set<WorkoutSession>();
    public DbSet<WorkoutSet> WorkoutSets => Set<WorkoutSet>();
    public DbSet<QuestTemplate> QuestTemplates => Set<QuestTemplate>();
    public DbSet<QuestInstance> QuestInstances => Set<QuestInstance>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<GemTransaction> GemTransactions => Set<GemTransaction>();
    public DbSet<ShopItem> ShopItems => Set<ShopItem>();
    public DbSet<Food> Foods => Set<Food>();
    public DbSet<MealLog> MealLogs => Set<MealLog>();
    public DbSet<DailySettlement> DailySettlements => Set<DailySettlement>();
    public DbSet<HpEvent> HpEvents => Set<HpEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Character>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("\"Status\" = 0"); // one living character per user
            e.Property(x => x.Name).HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<int>();
        });
        b.Entity<PlayerProfile>().HasKey(x => x.UserId);
        b.Entity<BodyMetric>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Date }).IsUnique();
            e.Property(x => x.WeightKg).HasPrecision(5, 1);
            e.Property(x => x.WaistCm).HasPrecision(5, 1);
            e.Property(x => x.BodyFatPct).HasPrecision(4, 1);
            e.Property(x => x.SleepHours).HasPrecision(3, 1);
        });
        b.Entity<PlayerProfile>().Property(x => x.HeightCm).HasPrecision(5, 1);
        b.Entity<StatSnapshot>().HasIndex(x => new { x.CharacterId, x.Date }).IsUnique();
        b.Entity<Title>().HasIndex(x => x.Code).IsUnique();
        b.Entity<CharacterTitle>().HasKey(x => new { x.CharacterId, x.TitleId });
        b.Entity<AvatarModel>().HasIndex(x => x.Code).IsUnique();
        b.Entity<AvatarEvolution>().HasIndex(x => new { x.AvatarModelId, x.Stage }).IsUnique();
        b.Entity<OnboardingStep>().HasIndex(x => x.Code).IsUnique();
        // Foreign keys whose names don't follow the EF convention (StepId, QuestionId, SessionId).
        b.Entity<OnboardingStep>().HasMany(x => x.Questions).WithOne().HasForeignKey(x => x.StepId);
        b.Entity<OnboardingQuestion>().HasMany(x => x.Options).WithOne().HasForeignKey(x => x.QuestionId);
        b.Entity<WorkoutSession>().HasMany(x => x.Sets).WithOne().HasForeignKey(x => x.SessionId);
        b.Entity<PlayerAnswer>().HasOne<OnboardingQuestion>().WithMany().HasForeignKey(x => x.QuestionId);
        b.Entity<QuestInstance>().HasOne<Character>().WithMany().HasForeignKey(x => x.CharacterId);
        b.Entity<StatSnapshot>().HasOne<Character>().WithMany().HasForeignKey(x => x.CharacterId);
        b.Entity<CharacterTitle>().HasOne<Character>().WithMany().HasForeignKey(x => x.CharacterId);
        b.Entity<OnboardingQuestion>().HasIndex(x => x.Code).IsUnique();
        b.Entity<PlayerAnswer>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.QuestionId }).IsUnique();
            e.Property(x => x.ValueJson).HasColumnType("jsonb");
        });
        b.Entity<OnboardingProgress>().HasKey(x => x.UserId);
        b.Entity<Exercise>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.KcalPerRep).HasPrecision(5, 2);
        });
        b.Entity<WorkoutSet>().Property(x => x.KcalBurned).HasPrecision(7, 2);
        b.Entity<QuestTemplate>().HasIndex(x => x.Code).IsUnique();
        // One quest per slot per day: regenerating or a repeated job call can never duplicate quests.
        b.Entity<QuestInstance>().HasIndex(x => new { x.CharacterId, x.Date, x.Slot }).IsUnique();
        b.Entity<Wallet>().HasKey(x => x.UserId);
        b.Entity<GemTransaction>().HasIndex(x => new { x.UserId, x.CreatedAt });

        b.Entity<Food>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.ServingG).HasPrecision(6, 1);
            e.Property(x => x.Kcal).HasPrecision(6, 1);
            e.Property(x => x.ProteinG).HasPrecision(5, 1);
            e.Property(x => x.CarbG).HasPrecision(5, 1);
            e.Property(x => x.FatG).HasPrecision(5, 1);
        });
        b.Entity<MealLog>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Date });
            e.Property(x => x.Servings).HasPrecision(4, 2);
        });
        // One settlement per character per day: re-running the 4:00 job can never take HP twice.
        b.Entity<DailySettlement>(e =>
        {
            e.HasIndex(x => new { x.CharacterId, x.Date }).IsUnique();
            e.HasOne<Character>().WithMany().HasForeignKey(x => x.CharacterId);
            e.Property(x => x.QuestCompletion).HasPrecision(4, 3);
        });
        b.Entity<HpEvent>(e =>
        {
            e.HasIndex(x => new { x.CharacterId, x.Date });
            e.HasOne<Character>().WithMany().HasForeignKey(x => x.CharacterId);
        });
    }
}
