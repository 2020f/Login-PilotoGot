using Microsoft.AspNetCore.Authorization;

namespace Login.Areas.Identity.Pages.Account.Manage;

// The former profile link pointed at unconfigured Identity UI. Until a reviewed
// self-service profile exists, deny it explicitly rather than exposing defaults.
// Cover old /Manage/Index and descendant bookmarks with the same safe response.
[Authorize]
public sealed class IndexModel : DeniedPageModel { }
