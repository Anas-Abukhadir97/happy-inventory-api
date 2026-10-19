# Roles and notification hub

## Roles already enforced by the API

| Action | Admin | Management | Auditor |
| --- | :---: | :---: | :---: |
| View warehouses, items, dashboard, reports, supplier requests and own notifications | Yes | Yes | Yes |
| Create/change/delete inventory; prepare supplier requests; deduct stock through the eCommerce API | Yes | Yes | No |
| Create/change/deactivate users and assign roles | Yes | No | No |
| View application logs and compose notifications | Yes | No | No |

The first user is the protected Admin. Admin creates Management and Auditor accounts on **Users and access**; those roles are not additional default users. The API checks the role on every protected route. Changing a user's role or active state invalidates existing access. The browser hides actions the role cannot perform, but API authorization is the boundary.

## Notification behavior

Admin can compose an in-app notification for one active user. The recipient's hub shows the newest messages in ten-record pages, an unread count, and a read action. A server-sent event stream pushes a notification ID to the signed-in browser when a record is created or read; the browser reloads the list. The stream uses the existing bearer token in the request header, expires with that token, and reconnects with the existing browser session flow. It does not put tokens in the URL, localStorage, or sessionStorage. Other users cannot read or mark the recipient's notification. The list and read state persist in SQLite.

The sender may additionally request email and/or SMS. External channels are **off unless configured**. A request for an unconfigured channel returns 503 before creating a notification. The API sends to the selected user's account email and to an E.164 phone number entered for that message. `Sent` means the SMTP server or phone gateway accepted the request, not that the human received it. `Failed` records a provider error without logging the message or credentials. There is no automatic retry after an uncertain SMTP/SMS outcome, avoiding duplicate sends. In-app-only messages need no external service or cost.

Set these environment variables in the API process, using private local configuration or a secret store. Do not commit values:

| Channel | Variables |
| --- | --- |
| SMTP email | `Notifications__Email__Host`, `Notifications__Email__Port` (default `587`), `Notifications__Email__From`, `Notifications__Email__Username`, `Notifications__Email__Password` |
| Android SMS Gateway local mode | `Notifications__Sms__LocalUrl` (for example `http://192.168.1.50:8080/message`), `Notifications__Sms__Username`, `Notifications__Sms__Password` |

Email uses STARTTLS and the existing SMTP account, without a paid email API. An account provider may require an app password or may disallow SMTP; [Google's guidance](https://support.google.com/accounts/answer/185833?hl=en) explains its app-password prerequisites. SMS uses [SMS Gateway for Android's local server API](https://docs.sms-gate.app/getting-started/local-server/): install the Android app, connect phone and API host to the same trusted local network, enable Local Server, start it, then enter the local IP and Basic Authentication credentials in the API environment. The API accepts only a private/loopback IP ending in `/message`; it will not send the credentials to a public URL. No cloud SMS account is required, but the phone must stay on and the mobile carrier may charge for messages. Configure the Android app and test only with a phone number you control. No email or SMS is sent merely by deploying this feature.

The API routes and payloads are in the [0.3.0 draft OpenAPI contract](contracts/openapi.v1.json). The server-side stream is server-to-browser only; it is not a chat or a supplier-delivery channel. The supplier-order outbox still records local test delivery and does not contact a supplier.
