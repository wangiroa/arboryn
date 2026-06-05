# Security Audit Command

Perform security audit on: $ARGUMENTS

Check:
1. SQL injection prevention (parameterized queries)
2. XSS protection (input sanitization)
3. CSRF tokens
4. Authentication implementation
5. Authorization checks
6. Sensitive data exposure
7. Rate limiting
8. Dependency vulnerabilities: `npm audit`

Review:
- Environment variables (no secrets in code)
- Error messages (no stack traces in production)
- File upload handling
- API input validation