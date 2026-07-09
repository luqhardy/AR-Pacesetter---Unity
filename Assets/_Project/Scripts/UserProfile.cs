using UnityEngine;

/// <summary>
/// オンボーディング身体情報 (企画書 §5 スマホアプリUI):
/// 身長・体重・性別の初期入力値を PlayerPrefs に永続化する薄いラッパー。
/// 疲労推定やアバター調整のパーソナライズ係数として参照される。
/// </summary>
public static class UserProfile
{
    private const string KeyHeight = "ARV_UserHeightCm";
    private const string KeyWeight = "ARV_UserWeightKg";
    private const string KeyGender = "ARV_UserGender";

    public static float HeightCm
    {
        get => PlayerPrefs.GetFloat(KeyHeight, 170f);
        set => PlayerPrefs.SetFloat(KeyHeight, Mathf.Clamp(value, 100f, 230f));
    }

    public static float WeightKg
    {
        get => PlayerPrefs.GetFloat(KeyWeight, 60f);
        set => PlayerPrefs.SetFloat(KeyWeight, Mathf.Clamp(value, 30f, 200f));
    }

    /// <summary>"Male" / "Female" / "Other"</summary>
    public static string Gender
    {
        get => PlayerPrefs.GetString(KeyGender, "Other");
        set => PlayerPrefs.SetString(KeyGender, value);
    }

    public static bool IsOnboarded => PlayerPrefs.HasKey(KeyHeight);

    public static void Save() => PlayerPrefs.Save();
}
