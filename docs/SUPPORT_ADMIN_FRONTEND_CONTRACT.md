# Support Admin Frontend Contract

## Product Behavior

`support@craftoramedya.com` is an inbound support address, not a separate
webmail inbox.

Resend receives the email, the backend verifies the webhook, matches the sender
to a registered Craftora account, and creates a normal support ticket. Admins
manage every app-created and email-created conversation from the same panel.

When an admin replies:

- the reply is stored in the ticket conversation;
- the user receives an in-app/push notification;
- the user receives the same reply by branded email;
- replying to that email returns to `support@craftoramedya.com` and continues as
  an inbound support message flow.

## Admin Screens

### Support Inbox

Use:

```http
GET /api/admin/support/tickets?status=open&query=&page=1&pageSize=20
Authorization: Bearer <admin-token>
```

Supported status values:

- `open`
- `answered`
- `closed`

Display:

- subject
- customer name and email
- status
- last message sender
- last activity time

Recommended tabs:

- Open
- Answered
- Closed
- All

### Conversation Detail

Use:

```http
GET /api/admin/support/tickets/{ticketId}
Authorization: Bearer <admin-token>
```

Render `messages` chronologically. Visually distinguish `user` and `admin`
sender roles.

### Reply

Use:

```http
POST /api/admin/support/tickets/{ticketId}/reply
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "message": "Craftora destek yaniti"
}
```

Reply length: 1 to 5000 characters.

Closed tickets reject replies. Reopen the ticket before replying.

### Change Status

Use:

```http
PUT /api/admin/support/tickets/{ticketId}/status
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "status": "closed"
}
```

Allowed values:

- `open`
- `answered`
- `closed`

## User Screens

Users access only their own tickets:

```http
POST /api/support/tickets
GET /api/support/tickets
GET /api/support/tickets/{ticketId}
POST /api/support/tickets/{ticketId}/messages
```

The same conversation is available whether it began in the app or by email.

## Frontend Responsibilities

- Build the admin inbox and conversation UI.
- Poll/refetch after replies until realtime support updates are added.
- Show loading, empty, error, pagination, and closed-ticket states.
- Prevent reply submission while a request is in progress.
- Do not expose raw webhook or Resend data in the UI.
- Never allow non-admin roles to call admin support endpoints.
