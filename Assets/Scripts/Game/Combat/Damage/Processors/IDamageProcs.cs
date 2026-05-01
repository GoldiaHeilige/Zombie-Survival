public interface IDamageProcessor
{
    // Cho phép sửa e.baseDamage, cờ, v.v. Trả về false để HỦY damage (ví dụ friendly-fire).
    bool Process(ref DamageEvent e);
}
