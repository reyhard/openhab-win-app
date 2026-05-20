# openHAB Windows App Guide

The openHAB Windows app is a Windows 11 companion app for openHAB. It provides a tray flyout for quick sitemap control, a larger main window with embedded openHAB Main UI, Windows notifications, a command menu with global shortcuts, and optional Device Info Sync.

This guide describes how to configure and use the app. It is written for users of the Windows app, not for contributors working on the app source code.

<!-- Screenshot idea: Main window showing Main UI, left navigation, and optional sitemap pane. -->

## Requirements

- Windows 11.
- Microsoft Edge WebView2 Runtime for embedded openHAB Main UI.
- A reachable openHAB server.
- Optional: a myopenHAB account for cloud access and cloud notifications.

## Connection Setup

Open the app settings and configure the endpoints you want the app to use.

Typical local endpoint examples:

```text
http://192.168.1.3:8080
http://openhab:8080
```

Typical cloud endpoint:

```text
https://myopenhab.org
```

### Endpoint Mode

| Mode | Behavior |
| --- | --- |
| Automatic | The app tries the local endpoint first and uses the cloud endpoint if local access is unavailable. |
| Local only | The app uses only the local endpoint. |
| Cloud only | The app uses only the cloud endpoint. |

Automatic mode is usually the best choice for a laptop or tablet that is sometimes at home and sometimes away.

### Local API Token

If your openHAB server requires authentication for REST API access, create an API token in openHAB and enter it as the local API token in the app.

The app uses this token for local sitemap loading, Item commands, Device Info Sync, notification media that must be fetched from openHAB, and other local REST API calls.

For openHAB REST authentication details, see the [openHAB REST API documentation](https://www.openhab.org/docs/configuration/restdocs).

### Cloud Credentials

For myopenHAB access, enter your cloud endpoint, username or email address, and password in the app settings.

Cloud credentials are used for cloud endpoint access and cloud notification polling. If you choose Local only mode, new cloud notifications are not available.

## Tray Flyout And Main Window

The app has two main surfaces: the tray flyout and the main window.

### Tray Flyout

The tray flyout opens from the Windows tray and is meant for quick control. It uses the app's native sitemap renderer so common sitemap controls are available without opening the larger window.

The flyout currently does not host openHAB Main UI. Use the main window when you want the full Main UI experience.

<!-- Screenshot idea: Tray flyout with a native sitemap page and search button. -->

### Main Window

The main window is for longer sessions and configuration. It contains:

- embedded openHAB Main UI through WebView2,
- app Settings,
- the Notifications page,
- promoted Main UI pages discovered from openHAB,
- an optional native sitemap pane.

The optional sitemap pane can stay visible while you use Main UI, Settings, or Notifications.

For Main UI concepts, see the [openHAB Main UI documentation](https://www.openhab.org/docs/mainui/).

## Sitemaps

The Windows app can render openHAB sitemaps natively. Sitemaps are useful when you already use sitemap-based UIs or want a compact control surface in the tray flyout.

The app also adds Windows-specific navigation conveniences:

- breadcrumbs for subpage navigation,
- sitemap search.

For sitemap syntax and available sitemap element types, see the [openHAB Sitemaps documentation](https://www.openhab.org/docs/ui/sitemaps).
