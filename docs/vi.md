# Trình chỉnh sửa bản đồ RPG — vẽ bản đồ và đặt vật thể

Trình chỉnh sửa bản đồ RPG thay đổi các bản đồ ô gạch của thế giới RPG khi trò chơi đang chạy. Bạn vẽ nền và tường, đặt sprite và nhân vật lên các ô, vẽ va chạm, di chuyển điểm xuất hiện và đặt các trận chiến ngẫu nhiên. **Save** ghi các tệp của bản đồ, và trò chơi đi trên bản đồ mới ngay lập tức, không cần khởi động lại.

Mở từ hộp công cụ (cái búa trên thanh công cụ) với tên **RPG Map Editor**. Cửa sổ mở trên bản đồ mà trò chơi đang hiển thị; muốn mở bản đồ khác, dùng bộ chọn bản đồ.

## Bạn thấy gì trong cửa sổ

![RPG Map Editor trên ngôi nhà của trưởng làng trong RPG Plus: thanh công cụ có bộ chọn bản đồ, Save, Undo, Redo và các công cụ; bảng vật thể bên trái với tab Surfaces; bản đồ ở giữa; bảng thuộc tính bên phải có kích thước bản đồ, điểm xuất hiện, liên kết dịch chuyển và trận chiến.](assets/rpg-editor-open.jpg)

- **Thanh công cụ** — mod và tệp bản đồ (**+** tạo bản đồ mới), **Save**, **Undo**, **Redo**, năm công cụ, bộ chọn mã va chạm, bộ chọn phép chiếu (**Top-down** hoặc **Isometric**) và **Fit**, hiển thị toàn bộ bản đồ.
- **Bảng vật thể** (bên trái) — các vật thể RPG của mod, chia bốn tab theo hình dạng: **Surfaces** (nền), **Segments** (một ô tường hoặc hàng rào), **Sprites** (vật thể trên lưới) và **Actors** (nhân vật đứng trên một ô). Mỗi tab có tìm kiếm theo tên hoặc id và ảnh thu nhỏ ở mỗi dòng.
- **Bản đồ** (giữa) — bản đồ do chính bộ dựng hình của trò chơi vẽ. Khung mảnh cho biết các ô mà mỗi vị trí đặt chiếm; vị trí đang chọn được tô đầy.
- **Bảng thuộc tính** (bên phải) — vị trí đang chọn, thuộc tính của bản đồ, liên kết dịch chuyển và trận chiến.

Bộ chọn phép chiếu chỉ đổi cách vẽ. Bản đồ là một, và bạn có thể đổi phép chiếu ngay khi đang chỉnh sửa:

![Cùng ngôi nhà của trưởng làng ở phép chiếu isometric.](assets/rpg-editor-isometric.jpg)

## Các công cụ

- **Select** — nhấp vào một vị trí để chọn nó; kéo để chuyển sang ô khác; **Delete** xóa; **Esc** bỏ chọn.
- **Paint** — chọn một mặt nền hoặc một đoạn trong bảng, rồi nhấp hoặc kéo qua các ô. Mặt nền vẽ lớp đất, đoạn vẽ lớp kết cấu. Chuột phải xóa.
- **Place** — chọn một sprite hoặc nhân vật, rồi nhấp một ô. Bóng mờ của vật thể đi theo con trỏ; ô nào vật thể sẽ chặn thì bóng chuyển đỏ. Tab **Nhân vật trò chơi** (tab cuối) đặt một nhân vật làm đối tượng actor: đối tượng được ghi vào mod của bản đồ (`objects/characters.json`) ngay khi chọn, và nhân vật có kịch bản gia nhập sẽ gia nhập đội khi được nói chuyện (xem Trình chỉnh sửa nhân vật).
- **Collision** — vẽ khả năng đi qua của ô: **As objects (.)** để các vật thể quyết định, **Blocked (#)** chặn ô, **Forced open (o)** mở ô bắt buộc.
- **Spawn** — nhấp một ô để đặt điểm xuất hiện của người chơi tại đó. Nút **Pick a cell** trong bảng thuộc tính chuyển sang công cụ này.

Khi kéo để vẽ nền, các ô của nét vẽ được hiện ra. Mỗi nét vẽ, vị trí đặt, lần di chuyển, thay đổi va chạm và thay đổi điểm xuất hiện đều là một bước hoàn tác.

![Đang vẽ nền: trong tab Surfaces đã chọn đất, công cụ Paint đang bật và ô dưới con trỏ có viền.](assets/rpg-editor-paint.jpg)

Để đặt một sprite, chọn nó trong tab **Sprites**, bật **Place** rồi nhấp vào ô. Vị trí mới được chọn và bảng thuộc tính hiện ngay. Nhấp một vị trí đã có bằng **Select** sẽ hiện thuộc tính của nó trong bảng:

![Vị trí trên ngôi nhà của trưởng làng được chọn bằng Select: thùng gỗ được tô vàng, bảng thuộc tính ghi vật thể, id, đây là vị trí riêng của bản đồ, ô và nút Remove.](assets/rpg-editor-placement.jpg)

![Đặt một sprite: trong tab Sprites đã chọn thùng gỗ, công cụ Place đang bật; bóng mờ của thùng nằm trên ô dưới con trỏ.](assets/rpg-editor-place.jpg)

## Bảng thuộc tính

- **Vị trí** — tên và id của vật thể, cùng nguồn gốc của vị trí. Vị trí riêng của bản đồ ghi *This map's own placement*. Vị trí từ mod khác hiện tên mod đó: thay đổi của bạn được ghi như một bản vá trong mod của bạn, còn tệp của mod kia không đổi. Bạn có thể sửa **Cell X** và **Cell Y**, **Remove** để xóa, hoặc **Reset** để bỏ bản vá của mod bạn, để trò chơi thấy lại vị trí của chủ sở hữu.
- **Tương tác** — điều xảy ra khi người chơi dùng hoặc bước lên vị trí đặt này. **Làm** gồm **Trao vật phẩm** (chọn vật phẩm và **Số lượng**), **Dẫn tới ô hoặc bản đồ khác** (chọn bản đồ, đích X và Y, hướng nhìn và **Chọn ô trên bản đồ**), **Mở hội thoại** (chọn kịch bản), **Mở cửa hàng** (chọn cửa hàng) hoặc **Không gì**. **Khi** là lúc dùng (E hoặc F khi quay mặt về đối tượng) hoặc bước lên. Lựa chọn chỉ thay tương tác của chính đối tượng cho vị trí đặt này, nên lệnh riêng của rương cũng bị thay. Để chọn ô trên bản đồ khác, hãy lưu trước: trình soạn mở bản đồ đó, bạn nhấp vào ô tới, và bản đồ đang sửa mở lại với đích đã ghi (Ctrl+S để lưu; Esc để hủy). Với vị trí đặt của mod khác đã có tương tác riêng, lựa chọn làm mất một trường của nó sẽ bị từ chối vì bản vá không thể bỏ nó; hãy sửa tệp của chủ sở hữu.
- **Bản đồ** — kích thước theo ô, điểm xuất hiện (**Chọn ô**), **Biến thể** (diện mạo đối tượng trong biến thể bản đồ này) và **Không khí** (ví dụ `woodland`; không có thì để trống). Cả hai đều chọn từ danh sách.
- **Liên kết dịch chuyển** — dịch chuyển của bản đồ này dẫn tới đâu và dịch chuyển nào dẫn tới đây. Trên bản đồ, mũi tên xanh đi từ cửa tới ô đích trên bản đồ này, hoặc rời bản đồ về phía tên bản đồ khác; mũi tên xanh lá đến từ bản đồ khác; mũi tên đỏ là đích bị hỏng. Danh sách hiển thị cùng các liên kết (→ đi ra, ← đi vào). Nhấp vào liên kết để mở bản đồ đích. Dấu đỏ nghĩa là đích bị hỏng: không có bản đồ, ô nằm ngoài bản đồ, hoặc ô bị chặn.
- **Trận chiến** — chỉ hiện khi trò chơi có tích hợp trận chiến ngẫu nhiên (`rpg__battles`). **Steps per battle** là số bước trung bình giữa hai trận (0 tắt trận chiến). **Enemy groups** mỗi dòng một nhóm, các kẻ địch cách nhau bằng dấu phẩy; **Apply encounters** ghi lại.
- **Thêm nhóm kẻ địch...** (trong cùng mục) thêm một dòng với kẻ địch đó thành nhóm riêng; sửa dòng để đưa nhiều kẻ địch vào một nhóm.
- **Xem trước trạng thái** — với vị trí đặt có đối tượng có trạng thái hoặc tương tác dùng cờ `self.`, bảng thuộc tính liệt kê các cờ đó thành ô đánh dấu. Đánh dấu một cờ sẽ hiện trạng thái áp dụng theo đúng quy tắc của trò chơi. Đây chỉ là xem trước: giá trị giữ trong lúc cửa sổ mở, không lưu gì, và cờ của trò chơi không đổi. Canvas vẫn vẽ hình của đối tượng: sprite không đổi theo trạng thái.

![Bản đồ làng của rpg_plus_game: bảng thuộc tính liệt kê các liên kết dịch chuyển, đi ra (→, với bản đồ và ô đích) và đi vào (←).](assets/rpg-editor-village.jpg)

## Lưu và những tệp nào thay đổi

- **Save** (Ctrl+S) kiểm tra bản đồ và các vị trí trước. Nếu có lỗi, việc lưu dừng lại và dòng trạng thái nêu lỗi đó. Nếu không, trình chỉnh sửa chỉ ghi các tệp đã thay đổi, vào mod bạn đã chọn.
- Bản đồ mới tạo bằng **+** (16 × 12 ô, điểm xuất hiện ở 1,1) được ghi khi lưu lần đầu cùng với cảnh của nó. Từ đó trò chơi biết bản đồ ngay lập tức.
- Bạn có thể mở bản đồ của một mod mà bạn phụ thuộc vào. Vẽ trên bản đồ đó được ghi thành các nét trong một bản vá của mod bạn; tệp bản đồ của chủ sở hữu giữ nguyên từng byte.
- Không thể thêm vị trí vào bản đồ của mod khác. Chỉ có thể lưu vào các mod được cài đặt dưới dạng thư mục.

## Chưa có

Các mục sau không có trong phiên bản hiện tại; đừng tìm kiếm:

- đổi tên id của vị trí hoặc bản đồ, có cập nhật các tham chiếu;
- chọn theo hình chữ nhật, sao chép và dán vị trí;
- mở một vật thể đã đặt trong Trình chỉnh sửa đối tượng RPG ngay từ bản đồ;
- trạng thái và biến thể của vị trí đặt: xem trước trạng thái đọc các trạng thái riêng của đối tượng; ghi đè trạng thái theo vị trí và biến thể không sửa ở đây;
- vẽ mặt nạ hiệu ứng khí quyển lên bản đồ — không chỉnh sửa ở đây; để chỉnh diện mạo hãy dùng Effects Editor;
- **Tương tác**, vật phẩm nhặt: **Đặt lên bản đồ** trong trình soạn vật phẩm đưa đối tượng nhặt lên bản đồ; vật phẩm được trao khi vị trí đặt có tương tác **Trao vật phẩm** (đặt trong bảng thuộc tính).

## Khi có lỗi

1. **Bản đồ không có trong danh sách tệp.** Nó không nằm trong mod đã chọn cũng không nằm trong các mod phụ thuộc của mod đó.
2. **Save không làm gì và dòng trạng thái báo lỗi.** Một bản đồ hoặc một vị trí không hợp lệ; sửa ô hoặc giá trị mà dòng trạng thái nêu ra.
3. **Trò chơi không biết bản đồ mới.** Hãy lưu trước: bản đồ và cảnh chỉ được ghi ở lần lưu đầu tiên.
4. **Vị trí bị từ chối.** Vật thể không phải sprite hoặc nhân vật, ô nằm ngoài bản đồ, hoặc bản đồ thuộc mod khác.
5. **Không có bảng trận chiến.** Trò chơi không có trận chiến ngẫu nhiên (`rpg__battles`).
