# RPG Map Editor — vẽ bản đồ ô và đặt sự kiện

Trình sửa bản đồ RPG hiệu chỉnh các bản đồ ô của nền RPG ngay khi trò chơi đang chạy: ba lớp ô, tường và lối đi, điểm xuất phát của người chơi, trận đấu ngẫu nhiên và các sự kiện — nhân vật, biển hiệu, cửa, vật phẩm nhặt được. Lưu xong bản đồ được cập nhật trong game ngay lập tức, còn **Chơi** cho bạn đi lại trên đó.

Công cụ mở từ hộp công cụ (chiếc búa trên thanh công cụ) với tên **Trình sửa bản đồ RPG**, tác động lên bản đồ mà người chơi đang đứng.

## Một lần sửa và kết quả trong game

Nhà trưởng lão trong RPG Plus trước khi sửa:

![Nhà trưởng lão trong game: căn phòng gỗ, trưởng lão đứng phía trong, bên phải là một chiếc rương.](assets/house-before.jpg)

Trong trình soạn thảo: tab **Appearance** (Ngoại hình), sprite của thương nhân, **Place NPC** (Đặt làm NPC), rồi nhấp vào một ô trống. Sự kiện mới được chọn trên lớp **Sự kiện**, thuộc tính nằm bên phải:

![Trình sửa bản đồ RPG đang mở rpg_demo_game / rpg_elder_house.json: danh sách Appearance chọn merchant · village/merchant, trưởng lão và thương nhân mới được viền như các sự kiện, sự kiện của các tệp khác có viền nét đứt; bên phải là thuộc tính bản đồ (tileset village, 12 × 10, điểm xuất phát 6, 8 hướng lên) và sự kiện mới npc_village_merchant tại 9, 3 với charset village/merchant, hướng xuống, kích hoạt bằng phím hành động (E/F). Dòng trạng thái báo "merchant" đã được đặt làm NPC.](assets/editor-npc.jpg)

Sau khi **lưu** (Ctrl+S), thương nhân đã đứng trong nhà:

![Cùng căn phòng trong game, giờ có thương nhân ở bên phải bức tường sau.](assets/house-after.jpg)

Lời thoại và hành vi của anh ta được đặt trong thuộc tính sự kiện: kịch bản, điều kiện, cách kích hoạt.

## Các lớp và công cụ

- **Nền**, **Chính**, **Phía trước** — các lớp ô. Nền và Chính có thể chặn đường bằng ô đặc; Phía trước vẽ đè lên nhân vật và không bao giờ chặn.
- **Va chạm** — vẽ đè lên các ô: *Tường* (luôn bị chặn), *Lối đi* (luôn đi được) hoặc *Tự động* (theo các ô).
- **Sự kiện** — nhấp để chọn hoặc kéo, nhấp đúp vào ô trống để thêm, Del để xóa.
- **Bút chì**, **Tẩy**, **Tô**; nút chuột phải xóa, Alt+nhấp lấy ô, phím 1–5 đổi lớp, B/E/G đổi bút.
- **Hoàn tác**/**Làm lại** (Ctrl+Z / Ctrl+Y), lưới, thu phóng (Ctrl+con lăn).

![Lớp Va chạm của nhà trưởng lão: các bức tường được tô đỏ thành Tường, sàn mở.](assets/collision.jpg)

Các tab bên trái: **Ô** (bảng màu của tileset bản đồ, vật thể nguyên vẹn như ngôi nhà hay cái cây có thể đóng dấu một lần), **Nhân vật** (nhân vật có chỉ số chiến đấu, đặt làm kẻ địch), **Vật phẩm** (đặt làm đồ nhặt kèm số lượng) và **Appearance** (mọi sprite của các mod đã nạp, đặt làm NPC).

Cột bên phải chứa thuộc tính bản đồ: khóa tên, tileset, kích thước, thu phóng, ánh sáng nền cố định và bầu không khí, điểm xuất phát cùng hướng nhìn, và trận đấu ngẫu nhiên (khoảng mỗi N bước, các nhóm địch dạng `slime; wolf,slime`).

## Nhìn từ trên và đẳng cự

Trình soạn thảo vẽ bản đồ đúng theo cách game vẽ. Bộ chuyển đổi chế độ xem trên thanh công cụ chỉ đổi phần xem trước; dữ liệu bản đồ giữ nguyên. Ngôi nhà trong bản xem trước đẳng cự:

![Nhà trưởng lão trong bản xem trước đẳng cự của trình soạn thảo: cùng căn phòng vẽ theo góc đẳng cự, bảng màu tileset bên trái.](assets/editor-isometric.jpg)

Chế độ xem của game do cài đặt bộ sưu tập `tile.renderer` (`topdown` hoặc `isometric`) quyết định; `--editor-renderer=isometric` mở trình soạn thảo ngay ở bản xem trước đẳng cự.

## Tệp nào được ghi

- Bản đồ được lưu vào `maps/<id>.json` của chính nó.
- Sự kiện được ghi vào tệp sự kiện riêng của bản đồ trong cùng mod (`map_events/<bản đồ>/…`). Các sự kiện do tệp khác hoặc mod khác thêm vào bản đồ được hiển thị mờ với viền nét đứt và không bao giờ bị ghi đè; không thể tái sử dụng id của chúng.
- **+** cạnh danh sách tệp tạo bản đồ mới trong mod đang chọn; sau khi lưu, game nhận nó là một cảnh ngay lập tức.
- Chỉ có thể ghi vào các mod được cài dưới dạng thư mục.

## Khi có trục trặc

1. **Chơi báo game không biết cảnh này.** Bản đồ cần `scenes/<id>.json` trong mod của nó.
2. **Không chọn được sự kiện.** Nó thuộc về tệp hoặc mod khác (viền nét đứt).
3. **NPC không nói chuyện.** Sự kiện của nó không có kịch bản, hoặc cách kích hoạt không phải phím hành động.
4. **Lưu làm thay cả những dòng bạn không đụng tới.** Trình soạn thảo ghi tệp theo thứ tự riêng và thêm các giá trị mặc định của sự kiện, nên diff có thể hiển thị nhiều hơn lần sửa của bạn.
