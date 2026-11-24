<div align="center">

# 🎵 Spotify Lyrics Desktop Overlay

### *Trang trí màn hình với lời bài hát Spotify đồng bộ thời gian thực & nền trong suốt*

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=for-the-badge&logo=windows)](https://github.com/LcChinh21/SpotifyLyricsOverlay)
[![.NET](https://img.shields.io/badge/.NET-10.0%20WPF-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](LICENSE)
[![Release](https://img.shields.io/github/v/release/LcChinh21/SpotifyLyricsOverlay?style=for-the-badge&color=1DB954)](https://github.com/LcChinh21/SpotifyLyricsOverlay/releases)

<br/>

**Spotify Lyrics Desktop Overlay** là ứng dụng hiển thị lời bài hát (synced lyrics) tự động đồng bộ theo bài hát đang phát trên **Spotify Desktop** dưới dạng **Overlay trong suốt (Transparent HUD)**, sử dụng **bộ phông chữ chuẩn gốc của Spotify**, mang lại trải nghiệm thẩm mỹ cao (aesthetic) để chill, nghe nhạc, làm việc hoặc chơi game.

</div>

---

## 🌟 Tính Năng Nổi Bật (Key Features)

### 1. 🎵 Tự Động Đồng Bộ Theo Spotify (Zero Configuration)
- Tích hợp trực tiếp với **Windows System Media Transport Controls (SMTC)** API.
- **Hoàn toàn không cần tạo app Spotify Developer hay cấu hình API Key phức tạp**.
- Tự động nhận diện Tên bài hát, Nghệ sĩ, Thời lượng, Ảnh bìa Album (Album Art) và tính toán thời gian phát chính xác đến từng mili-giây.

### 2. 🪟 Nền Hoàn Toàn Trong Suốt (Pure Transparent Background)
- Không có khung nền đen hay hộp xám che khuất màn hình. Lời bài hát bay lơ lửng trực tiếp trên Desktop Wallpaper của bạn.
- Hiệu ứng đổ bóng đa chiều (**Drop Shadow**) sắc nét giúp đọc rõ chữ trên mọi hình nền (kể cả nền sáng, anime, tranh phong cảnh).

### 3. 🅰️ Bộ Phông Chữ Gốc Của Spotify (Official Spotify Mix Fonts)
- **Tên bài hát**: Sử dụng phông **`Spotify Mix UI Title Bold`** (nhận diện thương hiệu mới nhất của Spotify).
- **Lời bài hát & Nghệ sĩ**: Sử dụng phông **`Spotify Mix UI`**, canh lề trái (**Left-aligned**) chuẩn 100% giao diện Spotify Lyrics.
- Dòng đang hát (**Active Line**) hiển thị to rõ, sáng rực rỡ; các dòng trước và sau mờ nhẹ tạo chiều sâu thị giác.

### 4. 🎨 Bảng Tùy Chỉnh Toàn Diện (Cỡ Chữ & Màu Sắc)
- Bấm nút **`🎨 Tùy chỉnh`** để mở bảng điều khiển trực quan:
  - **Cỡ chữ (Font Size)**: Thanh trượt mượt mà từ **18pt đến 48pt** kèm 4 nút chọn nhanh (*Nhỏ 22, Vừa 28, Lớn 34, To 42*).
  - **Màu sắc câu đang hát**: Bảng màu chọn nhanh (*Trắng Spotify, Xanh Spotify, Cyan Neon, Tím Lavender, Hồng Neon, Vàng Sunset, Cam, Đỏ*) hoặc **nhập bất kỳ mã màu Hex nào** (`#1DB954`, `#00FFCC`,...).
  - **Độ mờ các câu còn lại (Opacity)**: Tùy chỉnh độ mờ từ **15% đến 85%**.
  - Tự động lưu toàn bộ cài đặt vào máy tính (`settings.json`).

### 5. 🔒 Chế Độ Khóa Thông Minh & Hỗ Trợ Chơi Game (Smart Lock / Gaming Mode)
- **Khóa xuyên chuột (Click-Through)**: Nhấn **`Ctrl + Shift + L`** hoặc nút **🔒** trên thanh công cụ để khóa:
  - Mọi nút bấm và viền kéo sẽ **tự động ẩn hoàn toàn**, trả lại màn hình sạch đẹp nhất.
  - Chuột bấm xuyên qua chữ, không lo click nhầm khi đang chơi game (Valorant, CS2, LMHT,...) hay gõ code.
- **Rê chuột hiện nút mở khóa**: Khi đang khóa, chỉ cần rê chuột vào khu vực overlay sẽ tự động hiện nút **`🔓 Mở khóa`** ở góc trên bên phải để click mở khóa ngay tức thì!
- **Chống cướp tiêu điểm (`WS_EX_NOACTIVATE`)**: Overlay không bao giờ cướp focus của game hay ứng dụng khác.

### 6. ⏩ Click Để Tua Nhạc (Seek to Lyric)
- Nhấp chuột trực tiếp vào bất kỳ câu hát nào trên màn hình để Spotify tự động tua đến đúng giây đó.
- Nhấp vào thanh tiến độ (Progress bar) để tua nhanh bài hát.

---

## 📥 Tải Về & Cài Đặt (Quick Start)

### Cách 1: Tải Bản Đóng Gói Sẵn (Khuyên Dùng)
1. Truy cập mục [**Releases**](https://github.com/LcChinh21/SpotifyLyricsOverlay/releases/latest) và tải file **`SpotifyLyricsOverlay-v1.0.0.zip`**.
2. Giải nén vào một thư mục bất kỳ trên máy.
3. Nhấp đúp chuột vào file **`run.bat`** (hoặc `SpotifyLyricsOverlay.exe`) để chạy ngay!

### Cách 2: Tự Build Từ Mã Nguồn (Build from Source)
Yêu cầu: Máy tính đã cài đặt [.NET 10 SDK](https://dotnet.microsoft.com/download) trên Windows 10/11.

```powershell
# Clone repository
git clone https://github.com/LcChinh21/SpotifyLyricsOverlay.git
cd SpotifyLyricsOverlay

# Chạy trực tiếp
dotnet run

# Hoặc đóng gói bản Release
dotnet publish -c Release
```

---

## ⌨️ Phím Tắt & Thao Tác (Shortcuts)

| Thao tác | Chức năng |
|---|---|
| **`Ctrl + Shift + L`** | Bật / Tắt nhanh chế độ khóa ghim xuyên chuột (**Click-Through**) |
| **Rê chuột khi đang khóa** | Hiện nút **`🔓 Mở khóa`** ở góc trên phải |
| **Kéo chuột trái** | Di chuyển vị trí overlay trên màn hình *(khi đã mở khóa)* |
| **Kéo góc phải dưới** | Phóng to / Thu nhỏ kích thước khung hiển thị |
| **Cuộn chuột (Scroll)** | Xem trước các câu hát trên/dưới *(tự động quay lại sau 3s)* |
| **Click vào câu hát** | Tua nhạc Spotify đến đúng câu đó |
| **Click thanh tiến độ** | Tua nhanh bài hát |
| **Nút `🎨 Tùy chỉnh`** | Mở bảng chỉnh màu sắc, cỡ chữ và độ mờ |
| **Nút `A-` / `A+`** | Tăng / giảm cỡ chữ nhanh |
| **Nút `-0.5s` / `+0.5s`** | Cân chỉnh độ lệch nhịp của lời bài hát |
| **Nút `✨`** | Bật / Tắt giữa nền trong suốt tuyệt đối và nền kính mờ |

---

## 🎮 Hướng Dẫn Dành Cho Game Thủ (Valorant, CS2, Apex Legends)

Nếu bạn chơi game và muốn lyrics Spotify nổi lên trên bề mặt game:
1. Vào phần **Cài đặt hình ảnh trong Game** (Settings -> Video).
2. Chuyển chế độ hiển thị (**Display Mode**) từ *Toàn màn hình (Fullscreen)* sang **`Toàn màn hình không viền (Windowed Fullscreen / Borderless)`**.
3. Bật **`Ctrl + Shift + L`** để khóa ghim overlay, giờ bạn có thể lia chuột và bắn súng xuyên qua chữ mà không bị ảnh hưởng!

---

## 🏗️ Cấu Trúc Dự Án (Project Structure)

```
SpotifyLyricsOverlay/
├── Fonts/                               # Bộ phông chữ gốc của Spotify
│   ├── SpotifyMixUI-Bold.ttf
│   ├── SpotifyMixUI-Regular.ttf
│   ├── SpotifyMixUITitle-Bold.ttf
│   └── SpotifyMixUITitle-Extrabold.ttf
├── Helpers/
│   └── WindowHelper.cs                  # Win32 API: Topmost, Click-Through, Hotkeys
├── Models/
│   ├── AppSettings.cs                   # Cấu hình lưu trữ vị trí, màu sắc, cỡ chữ
│   ├── LyricLine.cs                     # Model dòng lời bài hát & timestamp
│   └── SongInfo.cs                      # Model thông tin bài hát & album art
├── Services/
│   ├── LyricsService.cs                 # Tải & đồng bộ lyrics từ LRCLIB CDN
│   ├── SettingsService.cs               # Đọc / Ghi cấu hình người dùng (JSON)
│   └── SmtcService.cs                   # Giao tiếp Windows SMTC (Spotify Transport Control)
├── MainWindow.xaml                      # Giao diện XAML WPF (HUD, Glassmorphism, Controls)
├── MainWindow.xaml.cs                   # Logic xử lý giao diện, animation cuộn mượt
├── run.bat                              # File khởi chạy 1-click
└── SpotifyLyricsOverlay.csproj          # Cấu hình dự án .NET 10 WPF
```

---

## 📄 Bản Quyền & Giấy Phép (License)

Dự án được phát hành theo giấy phép [MIT License](LICENSE). Hoàn toàn miễn phí cho mục đích cá nhân và phi thương mại.
Lưu ý: Spotify và các phông chữ liên quan thuộc bản quyền của Spotify AB.
