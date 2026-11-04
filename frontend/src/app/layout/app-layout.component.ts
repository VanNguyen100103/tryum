import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { AssistantChatComponent } from '../shared/assistant-chat/assistant-chat.component';

@Component({
  selector: 'app-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, AssistantChatComponent],
  templateUrl: './app-layout.component.html',
  styleUrl: './app-layout.component.scss',
})
export class AppLayoutComponent {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  protected readonly user = this.auth.user;
  protected readonly initials = this.auth.initials;
  protected readonly shopName = computed(() => this.auth.user()?.tenantName ?? '');
  protected readonly openMenu = signal('');

  protected isActive(route: string): boolean {
    const url = this.router.url;
    return url === route || url.startsWith(route + '/');
  }

  protected toggleMenu(name: string): void {
    this.openMenu.update((current) => current === name ? '' : name);
  }

  protected closeMenu(): void {
    this.openMenu.set('');
  }

  protected logout(): void {
    this.auth.logout().subscribe({
      next: () => void this.router.navigate(['/auth/login']),
      error: () => {
        this.auth.clearSession();
        void this.router.navigate(['/auth/login']);
      },
    });
  }
}
