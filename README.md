<div align="center">

# 🎵 Spotify Lyrics Desktop Overlay

### *Trang trí màn hình với lời bài hát Spotify đồng bộ thời gian thực, dịch song ngữ & nền trong suốt*

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=for-the-badge&logo=windows)](https://github.com/LcChinh21/SpotifyLyricsOverlay)
[![.NET](https://img.shields.io/badge/.NET-10.0%20WPF-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](LICENSE)
[![Release](https://img.shields.io/github/v/release/LcChinh21/SpotifyLyricsOverlay?style=for-the-badge&color=1DB954)](https://github.com/LcChinh21/SpotifyLyricsOverlay/releases)

<br/>

**Spotify Lyrics Desktop Overlay** là ứng dụng hiển thị lời bài hát (synced lyrics) tự động đồng bộ theo bài hát đang phát trên **Spotify Desktop** dưới dạng **Overlay trong suốt (Transparent HUD)**, sử dụng **bộ phông chữ của Spotify**, mang lại trải nghiệm thẩm mỹ cao (aesthetic) để chill, nghe nhạc, làm việc hoặc chơi game.

</div>

---

## 🌟 Tính Năng Nổi Bật (v1.1.0)

### 1. 🎵 Tự Động Đồng Bộ & Đổi Màu Theo Ảnh Bìa Album
- Tích hợp trực tiếp với **Windows System Media Transport Controls (SMTC)** API — **không cần API Key hay đăng nhập**.
- **Dynamic Adaptive Color:** Tự động trích xuất tông màu nổi bật từ ảnh bìa Album đang phát để tô cho dòng đang hát và thanh tiến độ.

### 2. 🌐 Dịch Lời Bài Hát Song Ngữ (Bilingual Lyrics)
- Tự động dịch lời bài hát quốc tế (US-UK, K-Pop, J-Pop, C-Pop...) sang **Tiếng Việt** hoặc **Tiếng Anh** và hiển thị phụ đề nhỏ ngay dưới từng câu hát.
- Tự nhận diện ngôn ngữ gốc để bỏ qua dịch trùng lặp đối với nhạc Việt.

### 3. 🪟 Nền Trong Suốt & Bộ Chữ Spotify
- Nền hoàn toàn trong suốt với hiệu ứng đổ bóng (**Drop Shadow**) sắc nét, đọc rõ trên mọi hình nền.
- Sử dụng bộ phông **`Spotify Mix UI`** & **`Spotify Mix UI Title`** canh lề trái chuẩn giao diện Spotify.
- **2 chế độ hiển thị:**
  - **Spotify Scroll:** Danh sách cuộn dọc nhiều dòng mượt mà.
  - **Mini Mode (Dynamic Island):** Hiển thị thu gọn 1–2 dòng tối giản.

### 4. 📊 Thanh Sóng Nhạc (Audio Visualizer - Tùy Chọn)
- Có sẵn **Audio Visualizer 12 dải tần** phản hồi theo âm thanh hệ thống qua WASAPI Loopback.
- **Mặc định TẮT** để giữ giao diện tối giản; bạn có thể bật/tắt tùy ý trong mục `🎨 Tùy chỉnh`.

### 5. 💾 Đa Nguồn Lyrics, Cache Offline & Nạp File `.lrc`
- Tự động tìm kiếm đa tầng: **Offline Cache (`.lrc`)** -> **Spotify `sp_dc` (tùy chọn)** -> **LRCLIB** -> **NetEase**.
- Tự động lưu cache `.lrc` để nghe lại không cần mạng.
- Hỗ trợ **kéo-thả file `.lrc`** trực tiếp vào cửa sổ hoặc chọn qua nút `📂 Nạp file .lrc thủ công`.

### 6. 🔒 Khóa Thông Minh, System Tray, Auto-Hide & Presets
- **Smart Lock (`Ctrl + Shift + L`):** Ẩn toàn bộ nút bấm và bật xuyên chuột (Click-Through). Rê chuột vào góc trên phải khi đang khóa để hiện nút **`🔓 Mở khóa`**.
- **System Tray & Khởi động cùng Windows:** Thu gọn gọn gàng xuống khay hệ thống, hỗ trợ tự chạy khi mở máy.
- **Auto-Hide on Pause:** Tùy chọn tự động ẩn overlay sau 8 giây khi tạm dừng nhạc và tự hiện lại khi phát tiếp.
- **4 Preset nhanh:** `Chill Desktop`, `Gaming / Valorant`, `Mini Island`, `Karaoke`.

---

## 📥 Tải Về & Cài Đặt (Quick Start)

1. Truy cập mục [**Releases**](https://github.com/LcChinh21/SpotifyLyricsOverlay/releases/latest) và tải file **`SpotifyLyricsOverlay-v1.1.0.zip`**.
2. Giải nén vào một thư mục bất kỳ trên máy.
3. Nhấp đúp chuột vào file **`run.bat`** (hoặc `SpotifyLyricsOverlay.exe`) để chạy ngay!

### Tự Build Từ Mã Nguồn
Yêu cầu: [.NET 10 SDK](https://dotnet.microsoft.com/download) trên Windows 10/11.
```powershell
git clone https://github.com/LcChinh21/SpotifyLyricsOverlay.git
cd SpotifyLyricsOverlay
dotnet run
```

---

## ⌨️ Phím Tắt & Thao Tác

| Thao tác | Chức năng |
|---|---|
| **`Ctrl + Shift + L`** | Bật / Tắt nhanh chế độ khóa xuyên chuột (**Click-Through**) |
| **Rê chuột khi đang khóa** | Hiện nút **`🔓 Mở khóa`** ở góc trên phải |
| **Kéo chuột trái** | Di chuyển vị trí overlay *(khi đã mở khóa)* |
| **Kéo góc phải dưới** | Thay đổi kích thước khung hiển thị |
| **Kéo thả file `.lrc`** | Nạp lời bài hát thủ công cho bài đang phát |
| **Click vào câu hát** | Tua nhạc Spotify đến đúng câu đó |
| **Nút `Spotify / Mini`** | Chuyển nhanh giữa chế độ cuộn nhiều dòng và Mini 1–2 dòng |
| **Nút `🎨 Tùy chỉnh`** | Mở bảng chỉnh màu, cỡ chữ, dịch song ngữ, sóng nhạc, presets |
| **Nút `—` / Đóng `X`** | Thu gọn xuống khay hệ thống (System Tray) |

---

## 🎮 Hướng Dẫn Dành Cho Game Thủ (Valorant, CS2, Apex Legends)
1. Trong **Cài đặt hình ảnh** của game, chọn chế độ **`Windowed Fullscreen` (Toàn màn hình không viền)**.
2. Chọn Preset **`🎮 Gaming`** hoặc nhấn **`Ctrl + Shift + L`** để khóa xuyên chuột.
