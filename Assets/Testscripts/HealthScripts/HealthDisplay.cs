using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Reusable, optional health UI. Place on a HUD or world-space Canvas and
/// assign a Health, Slider and TMP text. Both read the exact same health value.
/// </summary>
public class HealthDisplay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Health health;
    [SerializeField] private Slider healthSlider;
    [SerializeField] private TMP_Text healthNumber;

    [Header("Number Format")]
    [SerializeField] private bool showMaxHealth = true;
    [SerializeField] private bool roundUpHealth = true;

    private void Awake()
    {
        if (health == null)
            health = GetComponentInParent<Health>();
    }

    private void OnEnable()
    {
        if (health == null) health = GetComponentInParent<Health>();
        if (health != null)
        {
            health.OnHealthChanged.AddListener(Refresh);
            Refresh(health.CurrentHealth); // Fix: initial value, not only later damage.
        }
        else
        {
            Debug.LogWarning("HealthDisplay needs a Health reference in its Inspector.", this);
        }
    }

    private void OnDisable()
    {
        if (health != null) health.OnHealthChanged.RemoveListener(Refresh);
    }

    public void SetHealthSource(Health newHealth)
    {
        if (isActiveAndEnabled && health != null)
            health.OnHealthChanged.RemoveListener(Refresh);

        health = newHealth;
        if (isActiveAndEnabled && health != null)
            health.OnHealthChanged.AddListener(Refresh);

        if (health != null) Refresh(health.CurrentHealth);
    }

    public void Refresh(float currentValue)
    {
        if (health == null) return;

        if (healthSlider != null)
        {
            healthSlider.minValue = 0f;
            healthSlider.maxValue = Mathf.Max(0.0001f, health.MaxHealth);
            healthSlider.SetValueWithoutNotify(currentValue);
        }

        if (healthNumber != null)
        {
            string current = roundUpHealth
                ? Mathf.CeilToInt(currentValue).ToString()
                : currentValue.ToString("0.#");
            string maximum = roundUpHealth
                ? Mathf.CeilToInt(health.MaxHealth).ToString()
                : health.MaxHealth.ToString("0.#");

            healthNumber.text = showMaxHealth ? current + " / " + maximum : current;
        }
    }
}
