namespace GtaVietLife.Launcher.Services;

/// <summary>The seven states the brief requires; every status message maps to exactly one.</summary>
public static class LauncherStates {
 public const string Ready="Sẵn sàng",Checking="Đang kiểm tra",Downloading="Đang tải",Installing="Đang cài",
  NeedsRepair="Cần sửa chữa",Incompatible="Không tương thích",Error="Lỗi";
 public static readonly string[] All={Ready,Checking,Downloading,Installing,NeedsRepair,Incompatible,Error};

 public static string Of(string title){
  var t=(title??"").ToUpperInvariant();
  if(t.StartsWith("SẴN SÀNG")||t.StartsWith("ĐANG TRONG GAME")||t.StartsWith("KHÔNG CÓ BẢN MỚI"))return Ready;
  if(t.StartsWith("ĐANG TẢI"))return Downloading;
  if(t.StartsWith("ĐANG KHỞI ĐỘNG")||t.StartsWith("ĐANG CÀI")||t.StartsWith("ĐANG KHÔI PHỤC")||t.StartsWith("ĐANG DỪNG"))return Installing;
  if(t.StartsWith("KHÔNG TƯƠNG THÍCH"))return Incompatible;
  if(t.StartsWith("BỘ CÀI BỊ HỎNG")||t.StartsWith("CẦN")||t.StartsWith("CHẾ ĐỘ HẠN CHẾ")||t.StartsWith("ĐÃ HỦY"))return NeedsRepair;
  if(t.StartsWith("ĐANG"))return Checking;
  return Error;
 }
}
