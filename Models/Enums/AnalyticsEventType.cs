using NpgsqlTypes;

namespace CraftoraApi.Models.Enums;

public enum AnalyticsEventType
{
    [PgName("shop_visit")]
    ShopVisit,

    [PgName("product_view")]
    ProductView,

    [PgName("media_view")]
    MediaView,

    [PgName("add_to_cart")]
    AddToCart,

    [PgName("checkout_started")]
    CheckoutStarted,

    [PgName("purchase_completed")]
    PurchaseCompleted,

    [PgName("download_clicked")]
    DownloadClicked
}
