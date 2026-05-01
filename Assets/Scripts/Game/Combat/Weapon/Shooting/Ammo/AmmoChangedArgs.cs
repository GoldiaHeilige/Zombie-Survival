using System;

[Serializable]
public struct AmmoChangedArgs
{
    public string weaponId;      // ví dụ "AR_M4"
    public string runtimeGuid;   // khẩu đang báo (để phân biệt các bản sao)
    public int slotIndex;        // -1 nếu không gắn slot (vd vũ khí tạm)
    public int mag;              // đạn trong băng
    public int reserve;          // đạn dự trữ
    public int magSize;          // sức chứa băng

    public AmmoChangedArgs(string weaponId, string guid, int slot, int mag, int reserve, int magSize)
    {
        this.weaponId = weaponId;
        this.runtimeGuid = guid;
        this.slotIndex = slot;
        this.mag = mag;
        this.reserve = reserve;
        this.magSize = magSize;
    }
}
