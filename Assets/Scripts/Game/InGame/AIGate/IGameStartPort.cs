/// Cổng điều khiển "bắt đầu round" từ UI/console.
/// SPImpl: gọi trực tiếp RoundDirector.RequestManualStart().
/// MPImpl: Client gửi yêu cầu lên Host; Host quyết định và gọi start.
public interface IGameStartPort
{
    /// Yêu cầu bắt đầu (từ người chơi/console). Implementation tự xử lý SP/MP.
    void RequestStart();
}
