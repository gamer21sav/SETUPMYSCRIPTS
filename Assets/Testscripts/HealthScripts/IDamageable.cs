public interface IDamageable
{
    bool IsAlive { get; }
    float CurrentHealth { get; }
    float MaxHealth { get; }

    void TakeDamage(float amount);
}
