# Contrato Portal comercial v1

Implementación separada del CRM. JSON camelCase; errores `{error,code}`. No acepta JWT ni cookies CRM. Los DTO exactos están en `Envirapido.Portal.Contracts/PortalContracts.cs`; artefactos en `ArtifactsContracts.cs`.

Base `/portal-api/v1`. Cookie host-only HttpOnly; fetch con `credentials: include`. GET `/csrf` => `{requestToken}`; enviar `X-CSRF-TOKEN` en todo POST/PUT/DELETE de comprador, incluso registro/login/recuperación. Obtener otro token después de login/logout. Todos los cambios idempotentes requieren header `Idempotency-Key` (UUID estable por intento, no reintentar un POST automáticamente con otra clave).

| Ruta | Entrada / salida |
| --- | --- |
| GET `/catalog` | `PortalCatalogDto`: plans, updatedAt, policyVersions. Usar policies recibidas (por defecto terms/privacy `2026-11-17`). |
| POST `/auth/register` | `PortalRegisterInput` + Idempotency-Key => 201 `PortalAccountDto`; inicia sesión de portal, no activa recursos. |
| POST `/auth/login` | email,password,rememberMe => `PortalAccountDto`. |
| POST `/auth/logout` | `{}` =>204. |
| GET `/auth/me`, GET `/account` | `PortalAccountDto`: user,profile,access,requests. |
| PUT `/account` | `PortalProfileInput` => `PortalAccountDto`; no cambia email/credenciales/decisiones. |
| POST `/auth/confirm-email` | userId,token => `{message}`. |
| POST `/auth/resend-confirmation` | `{}` con sesión => `{message}` genérico. |
| POST `/auth/forgot-password` | email => `{message}` genérico. |
| POST `/auth/reset-password` | userId,token,password,confirmPassword => `{message}`; revoca sesiones anteriores. |
| GET `/requests` | `{requests:PortalRequestDto[]}`. |
| POST `/requests` | planCode,planVersion + Idempotency-Key =>201 PortalRequestDto. |
| POST `/requests/{id}/payment-reports` | amount,paidOn(yyyy-MM-dd),method,reference,notes + Idempotency-Key =>201 PortalPaymentReportDto. Es solo reporte, nunca confirmación de pago; no adjuntos en esta versión. |
| GET `/subscription` | `{access,requests}`. |

Perfil: responsibleName,businessName,phone,province,city,operationType obligatorios; operationType texto (sin catálogo cerrado). estimatedStores,estimatedPilots,estimatedOrdersPerMonth enteros opcionales; rnc,billingAddress,notes opcionales. No credenciales ni identificadores CRM.

Estados: cuenta Active/Suspended y emailConfirmed independiente. Solicitud Submitted/InReview/NeedsInformation/Approved/Rejected/Cancelled. Pago NotReported/Reported/InReview/Rejected/Confirmed/Voided/NotRequired. Aprobar una solicitud no confirma pago ni define fechas. access.allowed requiere TODOS los controles. Reasons: ACTIVE,ACCOUNT_SUSPENDED,EMAIL_UNCONFIRMED,REQUEST_PENDING,PAYMENT_PENDING,NOT_CONFIGURED,NOT_STARTED,EXPIRED,SUSPENDED,REVOKED. El único CRM URL permitido es `https://envirapido.com/Identity/Account/Login`, y solo se devuelve cuando allowed=true. Vigencia startsOn/endsOn RD inclusivos, almacenada con extremo UTC final exclusivo; no modifica CRM.

Administración `/portal-api/internal/admin`: exclusivamente secreto de servidor `X-Envirapido-Portal-Key` y operador `X-Envirapido-Operator`. No se acepta cookie comprador ni CRM como autorización; el bridge CRM verifica SuperAdmin y CSRF. Todos los POST requieren Idempotency-Key.

| Ruta admin | Contrato |
| --- | --- |
| GET `/summary` | PortalAdminSummary |
| GET `/users?q=&status=&page=1` | PortalPage<PortalAdminUserDto> |
| POST `/users/{id}/status` | PortalAdminUserStatusInput |
| GET `/requests?q=&status=&page=1` | PortalPage<PortalAdminRequestDto> |
| GET `/requests/{id}` | PortalAdminRequestDetail |
| POST `/requests/{id}/decision` | PortalDecisionInput (InReview/NeedsInformation/Approved/Rejected/Cancelled) |
| POST `/payments/confirm` | PortalPaymentConfirmInput |
| POST `/payment-reports/{id}/decision` | PortalPaymentReportDecisionInput: decision InReview/Rejected, reason, version. Nunca confirma dinero. |
| POST `/payments/{id}/void` | PortalPaymentVoidInput |
| POST `/access` | PortalAccessInput; status Enabled/Suspended/Revoked; version actual o null al crear |
| GET `/catalog` | `{plans:PortalAdminCatalogEntry[]}` incluye fuente activa no publicada |
| POST `/catalog` | PortalCatalogPublicationInput; version actual o null al crear |
| GET `/audit?page=1` | PortalPage<PortalAuditDto> |

HTTP400 validación;401 sesión/clave;403 recursos;409 concurrencia, condiciones o idempotencia;429 límite;503 catálogo/correo/almacenamiento no disponibles. Cero significa sin límite en el catálogo. Precios siempre del servidor, snapshots inmutables.

Configuración propia: `ConnectionStrings:PortalConnection`; `Portal:AdminKey`, `Portal:CatalogUrl`, `Portal:CatalogKey`, `Portal:PublicBaseUrl`, `Portal:DataProtectionDirectory`, `Portal:TermsVersion`, `Portal:PrivacyVersion`, `Portal:Mail:*`. No `DefaultConnection`. Catálogo CRM: GET configurado a `/api/internal/portal/catalog`, header X-Envirapido-Catalog-Key => CrmPortalCatalog. Autorización y claves fuertes independientes; sin auto EnsureCreated/seeds ni credenciales comerciales demo en producción.

Compatibilidad de revisión: ReportDto incorpora status/resolution/version; confirmación admite reportId opcional para enlazar y resolver el reporte. Anular ese pago marca el reporte Voided. Detail.profile son los datos recibidos inmutables; currentProfile es el perfil actual opcional. AdminCatalogEntry.sourceActive indica si sigue en el catálogo de origen; se conservan publicaciones inactivas con snapshot histórico para retirarlas, sin habilitar compras. Summary.publishedPlans cuenta publicaciones configuradas, no visibilidad activa actual; pendingPayments cuenta reportes sin resolver.

PortalRequestDto.paymentReviewMessage opcional contiene el motivo público del último reporte rechazado/en revisión del estado mostrado; nunca incluye el actor interno. Cuando el estado financiero cambia, no se arrastra un motivo de rechazo antiguo a un pago ya confirmado.

PublicBaseUrl exige HTTPS en producción. CatalogUrl permite HTTP solo en loopback también en producción para el enlace privado entre servicios; HTTP remoto se rechaza. ForwardedProto y ForwardedFor se procesan únicamente desde proxies inmediatos exactos y con ForwardLimit=1. El proxy debe sobrescribir X-Forwarded-For con la IP del visitante ya validada; nunca trasladar una cabecera arbitraria. Esto evita compartir una sola cuota de login entre todos los compradores sin ampliar confianza a redes desconocidas.

El cliente de catálogo añade X-Forwarded-Proto=https exclusivamente en su destino HTTP loopback. El enlace interno continúa sin TLS: la cabecera conserva el enrutamiento HTTPS lógico del servicio público, y solo funciona si el receptor confía en la IP inmediata exacta. HTTPS normal no recibe esa cabecera. No se cambian Host, X-Forwarded-For, cookies ni Authorization del comprador. El receptor debe permitir explícitamente el host loopback usado en su AllowedHosts; mantener además los hosts públicos. No se amplían redes de confianza ni se siguen redirects entre servicios.
