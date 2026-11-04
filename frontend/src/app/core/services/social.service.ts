import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface FacebookConnection {
  connected: boolean;
  pageId: string | null;
  pageName: string | null;
  tokenSet: boolean;
}

export interface GeneratedPost {
  title: string;
  content: string;
  imageUrl: string;
}

@Injectable({ providedIn: 'root' })
export class SocialService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/v1/social/facebook`;

  status(): Observable<FacebookConnection> {
    return this.http.get<FacebookConnection>(this.baseUrl);
  }

  connect(pageId: string, accessToken: string): Observable<{ connected: boolean; pageId: string; pageName: string }> {
    return this.http.put<{ connected: boolean; pageId: string; pageName: string }>(this.baseUrl, { pageId, accessToken });
  }

  disconnect(): Observable<{ connected: boolean }> {
    return this.http.delete<{ connected: boolean }>(this.baseUrl);
  }

  publish(message: string, link: string | null): Observable<{ id: string; message: string }> {
    return this.http.post<{ id: string; message: string }>(`${this.baseUrl}/posts`, { message, link });
  }

  generate(topic: string): Observable<GeneratedPost> {
    return this.http.post<GeneratedPost>(`${this.baseUrl}/generate`, { topic });
  }

  publishWithImage(
    message: string,
    link: string | null,
    imageUrl: string | null,
  ): Observable<{ id: string; message: string }> {
    return this.http.post<{ id: string; message: string }>(`${this.baseUrl}/posts`, {
      message,
      link,
      imageUrl,
    });
  }
}
