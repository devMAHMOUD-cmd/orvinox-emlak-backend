# Security and Privacy Backlog

These items are intentionally deferred. The mobile client must not present them as active until the corresponding backend contract is implemented and tested.

## Planned Backend Work

- Add an API that lists the user's active devices and sessions.
- Add APIs to revoke a specific device session and all other sessions.
- Add password change and forgot-password flows.
- Add email OTP based two-factor authentication.
- Add alerts for sign-in from a new device.
- Add user block and unblock support.
- Add an endpoint that resets the user's discovery profile.
- Add a user data export flow.
- Add secure self-service account deletion with a 30-day recovery period.

## Delivery Rule

Each item requires its database/API implementation, authorization tests, production migration, mobile integration, and live end-to-end verification before it can be marked complete.
