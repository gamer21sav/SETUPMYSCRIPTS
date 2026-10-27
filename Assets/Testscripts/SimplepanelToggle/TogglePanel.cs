using UnityEngine;

public class TogglePanel : MonoBehaviour
{
    public GameObject targetObject;
    public GameObject[] otherObjectsToToggle;
    public bool initialState = false;

    void Start() => SetOpen(initialState);

    public void Toggle()
    {
        if (targetObject != null) SetOpen(!targetObject.activeSelf);
    }

    public void SetOpen(bool value)
    {
        if (targetObject == null) return;
        if (transform.IsChildOf(targetObject.transform))
        {
            Debug.LogWarning("Put TogglePanel on an always-active object outside its target panel.", this);
            return;
        }
        targetObject.SetActive(value);
        if (otherObjectsToToggle == null) return;
        foreach (GameObject obj in otherObjectsToToggle)
            if (obj != null && !transform.IsChildOf(obj.transform)) obj.SetActive(value);
    }
}
