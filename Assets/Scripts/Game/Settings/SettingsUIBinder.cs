//using UnityEngine;
//using UnityEngine.UIElements;

//public class SettingsUIBinder : MonoBehaviour
//{
//    [SerializeField] private GameObject panelSettings;

//    void Start()
//    {
//        // AUTO-CREATE nếu chưa có Instance
//        if (SettingsController.Instance == null && panelSettings != null)
//        {
//            Instantiate(panelSettings);
//        }

//        if (SettingsController.Instance == null)
//        {
//            Debug.LogError("[SettingsUIBinder] Cannot find or create SettingsController!");
//            return;
//        }

//        SettingsController.Instance.RegisterUI(panelSettings);
//    }
//}