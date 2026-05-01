using UnityEngine;

public static class DamageRouter
{
    private static IDamageDriver _driver;

    public static void SetDriver(IDamageDriver driver)
    {
        _driver = driver;
     //   Debug.Log($"[DMG-ROUTER] Set driver = {(_driver != null ? _driver.GetType().Name : "NULL")}");
    }

    public static DamageResult Apply(in DamageEvent e)
    {
        if (_driver == null)
        {
            // fallback an toàn: gọi core trực tiếp để không hard-break
         //   Debug.LogWarning("[DMG-ROUTER] No driver set. Fallback to direct DamageSystem.");
            return DamageSystem.Instance.Apply(e);
        }
        return _driver.Apply(e);
    }

    public static void ClearDriverIfEquals(IDamageDriver driver)
    {
        if (_driver == driver)
            _driver = null;
    }

}
