using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Stores health and raises events. UI is intentionally handled by HealthDisplay.
/// </summary>
public class Health : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField, Min(0f)] private float maxHealth = 50f;
    [SerializeField, Min(0f)] private float startingHealth = 50f;

    [Header("Death")]
    [SerializeField] private bool destroyOnDeath = true;
    [SerializeField] private bool respawnOnDeath = false;
    [SerializeField, Min(0f)] private float respawnDelay = 2f;
    [SerializeField] private Transform respawnPoint;

    public UnityEvent<float> OnHealthChanged = new UnityEvent<float>();
    public UnityEvent<float> OnDamaged = new UnityEvent<float>();
    public UnityEvent OnDied = new UnityEvent();
    public UnityEvent OnRespawned = new UnityEvent();

    public bool IsAlive => !isDead;
    public bool IsDead => isDead;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    private float currentHealth;
    private bool isDead;
    private Vector3 savedPosition;
    private Quaternion savedRotation;

    private void Awake()
    {
        maxHealth = Mathf.Max(0f, maxHealth);
        startingHealth = Mathf.Clamp(startingHealth, 0f, maxHealth);
        SaveRespawnTransform();
        currentHealth = startingHealth;
        isDead = currentHealth <= 0f;
        NotifyHealthChanged();
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(Respawn));
    }

    public void TakeDamage(float amount)
    {
        if (isDead || amount <= 0f) return;

        float previous = currentHealth;
        currentHealth = Mathf.Clamp(currentHealth - amount, 0f, maxHealth);
        float appliedDamage = previous - currentHealth;
        if (appliedDamage <= 0f) return;

        OnDamaged.Invoke(appliedDamage);
        NotifyHealthChanged();
        if (currentHealth <= 0f) Die();
    }

    // Compatibility alias for older Gun scripts using Takedamage instead of TakeDamage.
    public void Takedamage(float amount) => TakeDamage(amount);

    public void Heal(float amount)
    {
        if (isDead || amount <= 0f) return;
        float previous = currentHealth;
        currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maxHealth);
        if (!Mathf.Approximately(previous, currentHealth)) NotifyHealthChanged();
    }

    public void SetHealth(float value)
    {
        if (isDead) return;
        currentHealth = Mathf.Clamp(value, 0f, maxHealth);
        NotifyHealthChanged();
        if (currentHealth <= 0f) Die();
    }

    public void Die()
    {
        if (isDead) return;
        isDead = true;
        currentHealth = 0f;
        NotifyHealthChanged();
        OnDied.Invoke();

        if (respawnOnDeath)
            Invoke(nameof(Respawn), respawnDelay);
        else if (destroyOnDeath)
            Destroy(gameObject);
    }

    public void Respawn()
    {
        if (!isDead) return;

        CharacterController controller = GetComponent<CharacterController>();
        bool restoreController = controller != null && controller.enabled;
        if (restoreController) controller.enabled = false;

        transform.SetPositionAndRotation(savedPosition, savedRotation);

        if (restoreController) controller.enabled = true;

        currentHealth = maxHealth;
        isDead = currentHealth <= 0f;
        NotifyHealthChanged();
        OnRespawned.Invoke();
    }

    public void SetRespawnPoint(Transform point)
    {
        respawnPoint = point;
        SaveRespawnTransform();
    }

    public void SaveCurrentPositionAsRespawnPoint()
    {
        respawnPoint = null;
        SaveRespawnTransform();
    }

    private void SaveRespawnTransform()
    {
        Transform point = respawnPoint != null ? respawnPoint : transform;
        savedPosition = point.position;
        savedRotation = point.rotation;
    }

    private void NotifyHealthChanged() => OnHealthChanged.Invoke(currentHealth);
}
