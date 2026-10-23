using System.Text.Encodings.Web;
using UnityEngine;
[CreateAssetMenu(fileName = "Item", menuName = "NewItem")]
public class ItemSO : ScriptableObject
{
    public string itemName;
    public Sprite Icon;
    public int maxStackSize;
    public GameObject itemPrefab;
    public GameObject HandItemprefab;
}
