export interface AdminKeyFormProps {
  error?: string | null;
  isLoading?: boolean;
  onSubmit: (adminKey: string) => void;
}
