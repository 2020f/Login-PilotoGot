export interface PortalPlan {
  code: string;
  name: string;
  description: string;
  monthlyPrice: number;
  currency: string;
  maxStores: number;
  maxPilots: number;
  maxOrdersPerMonth: number;
  version: string;
  featured: boolean;
}
export interface Catalog {
  plans: PortalPlan[];
  updatedAt: string;
  policyVersions: { terms: string; privacy: string };
}
export interface PortalAuthOptions {
  emailConfirmationRequired: boolean;
  passwordRecoveryAvailable: boolean;
}
export interface Profile {
  responsibleName: string;
  businessName: string;
  phone: string;
  province: string;
  city: string;
  operationType: string;
  estimatedStores: number | null;
  estimatedPilots: number | null;
  estimatedOrdersPerMonth: number | null;
  rnc: string | null;
  billingAddress: string | null;
  notes: string | null;
}
export interface Access {
  allowed: boolean;
  reason: string;
  startsAt: string | null;
  endsAt: string | null;
  crmUrl: string | null;
}
export interface PlanRequest {
  id: string;
  createdAt: string;
  plan: PortalPlan;
  status: string;
  publicMessage: string | null;
  paymentStatus: string;
  paymentReviewMessage?: string | null;
  confirmedAmount: number;
  amountDue: number;
  version: string;
}
export interface Account {
  user: { id: string; email: string; emailConfirmed: boolean; status: string; emailConfirmationRequired?: boolean };
  profile: Profile;
  access: Access;
  requests: PlanRequest[];
}
export interface RegisterInput {
  email: string;
  password: string;
  confirmPassword: string;
  planCode: string;
  planVersion: string;
  profile: Profile;
  termsVersion: string;
  privacyVersion: string;
}
export interface DownloadRelease {
  id: string;
  versionName: string;
  versionCode: number;
  bytes: number;
  sha256: string;
  fileName: string;
  downloadPath: string;
  publishedAt: string;
}
export interface PaymentReportInput {
  amount: number;
  paidOn: string;
  method: string;
  reference: string;
  notes: string | null;
}
