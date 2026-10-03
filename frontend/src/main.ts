import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { isDevMode } from '@angular/core';

bootstrapApplication(App, appConfig)
  .then(() => {
    // Stagewise toolbar – chỉ chạy khi dev, không ảnh hưởng production
    if (isDevMode()) {
      import('@stagewise/toolbar').then(({ initToolbar }) => {
        initToolbar({
          plugins: [],   // thêm plugin sau nếu cần (e.g. custom context)
        });
      });
    }
  })
  .catch((err) => console.error(err));
