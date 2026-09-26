export interface ApiError {
  code: string;
  message: string;
}

export interface ApiResult<T> {
  isSuccess: boolean;
  value?: T;
  error?: ApiError;
}

export interface PaginatedResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}
