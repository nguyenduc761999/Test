using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Single sound entry: display name, clip, and volume.
/// </summary>
[Serializable]
[Preserve]
public class SoundEntry
{
    [SerializeField] string _name = string.Empty;
    [SerializeField] AudioClip _clip;
    [SerializeField] [Range(0f, 1f)] float _volume = 1f;

    public string Name
    {
        get => _name;
        set => _name = value;
    }

    public AudioClip Clip
    {
        get => _clip;
        set => _clip = value;
    }

    public float Volume
    {
        get => _volume;
        set => _volume = Mathf.Clamp01(value);
    }

    public SoundEntry()
    {
    }

    public SoundEntry(AudioClip clip)
    {
        _clip = clip;
        _name = clip != null ? clip.name : string.Empty;
        _volume = 1f;
    }
}

/// <summary>
/// Sound configuration: BGM, SFX, Button.
/// </summary>
[CreateAssetMenu(fileName = "SoundConfig", menuName = "Test/Sound Config", order = 0)]
[Preserve]
public class SoundConfig : ScriptableObject
{
    [SerializeField] List<SoundEntry> _bgmList = new List<SoundEntry>();
    [SerializeField] List<SoundEntry> _sfxList = new List<SoundEntry>();
    [SerializeField] List<SoundEntry> _buttonList = new List<SoundEntry>();

    public List<SoundEntry> BgmList => _bgmList;
    public List<SoundEntry> SfxList => _sfxList;
    public List<SoundEntry> ButtonList => _buttonList;

    /// <summary>
    /// Gets the list for the given category tab.
    /// </summary>
    public List<SoundEntry> GetList(SoundCategory category)
    {
        switch (category)
        {
            case SoundCategory.BGM:
                return _bgmList;
            case SoundCategory.SFX:
                return _sfxList;
            case SoundCategory.Button:
                return _buttonList;
            default:
                return null;
        }
    }

    /// <summary>
    /// Tìm SFX theo tên hiển thị trong danh sách.
    /// </summary>
    public SoundEntry FindSfxByName(string soundName)
    {
        if (string.IsNullOrEmpty(soundName) || _sfxList == null)
            return null;

        for (int i = 0; i < _sfxList.Count; i++)
        {
            SoundEntry entry = _sfxList[i];
            if (entry != null && entry.Name == soundName)
                return entry;
        }

        return null;
    }
}

/// <summary>
/// Đánh dấu field string chọn SFX từ SoundConfig trên cùng component.
/// </summary>
public class SfxSoundAttribute : PropertyAttribute
{
}

/// <summary>
/// Sound type matching the three manager tabs.
/// </summary>
public enum SoundCategory
{
    BGM,
    SFX,
    Button
}
