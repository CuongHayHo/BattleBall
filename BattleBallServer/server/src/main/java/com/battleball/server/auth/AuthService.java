package com.battleball.server.auth;

import com.battleball.proto.ChangePasswordError;
import com.battleball.proto.ChangePasswordRequest;
import com.battleball.proto.ChangePasswordResult;
import com.battleball.proto.LoginError;
import com.battleball.proto.LoginRequest;
import com.battleball.proto.LoginResult;
import com.battleball.proto.RegisterError;
import com.battleball.proto.RegisterRequest;
import com.battleball.proto.RegisterResult;
import com.battleball.server.ServerConfig;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.sql.SQLException;
import java.util.UUID;

/**
 * Register and login. Blocking (bcrypt, JDBC): call it only from the DB executor. Rules: docs/protocol/BattleBall.md 4.2 and docs/networking/BattleBall.md 5.
 * Failed logins never tell "unknown user" from "wrong password", and both cost the same time.
 */
public final class AuthService {
    private static final Logger LOG = LoggerFactory.getLogger(AuthService.class);
    private static final long WINDOW_MS = 10 * 60_000L;

    private final AccountRepository accounts;
    private final PasswordHasher hasher;
    private final int refreshTokenDays;
    private final RateLimiter loginByIp = new RateLimiter(30, WINDOW_MS);        // failed logins per address
    private final RateLimiter loginByUser = new RateLimiter(8, WINDOW_MS);       // failed logins per account name
    private final RateLimiter registerByIp = new RateLimiter(10, 60 * 60_000L);  // registrations per address per hour (GDD: limit per IP)
    private final RateLimiter changeByAccount = new RateLimiter(5, WINDOW_MS);      // wrong old passwords per account

    public AuthService(AccountRepository accounts, PasswordHasher hasher, ServerConfig config) {
        this.accounts = accounts;
        this.hasher = hasher;
        this.refreshTokenDays = config.refreshTokenDays();
    }

    /** What a successful login hands back to the connection. */
    public record LoginOutcome(LoginResult result, long accountId) {
        public boolean ok() {
            return result.getOk();
        }
    }

    public RegisterResult register(String ip, RegisterRequest req) {
        if (!AccountRules.validUsername(req.getUsername())) {
            return registerFail(RegisterError.USERNAME_INVALID);
        }
        if (!AccountRules.validPassword(req.getPassword())) {
            return registerFail(RegisterError.PASSWORD_INVALID);
        }
        if (!registerByIp.allowed(ip)) {
            return registerFail(RegisterError.REGISTER_RATE_LIMITED);
        }
        registerByIp.record(ip);
        try {
            AccountRepository.CreateResult r = accounts.createAccount(req.getUsername(), hasher.hash(req.getPassword()));
            if (r == AccountRepository.CreateResult.USERNAME_TAKEN) {
                return registerFail(RegisterError.USERNAME_TAKEN);
            }
            LOG.info("registered account {}", req.getUsername());
            return RegisterResult.newBuilder().setOk(true).build();
        } catch (SQLException e) {
            LOG.error("register failed", e);
            return registerFail(RegisterError.REGISTER_INTERNAL);
        }
    }

    /**
     * Changes the password of a logged-in account: the old password must be right (so a stolen open session cannot take the account), the new one follows
     * the registration rules. Every refresh token is revoked and a new one is returned for the device that changed it.
     */
    public ChangePasswordResult changePassword(long accountId, String deviceInfo, ChangePasswordRequest req) {
        try {
            if (!changeByAccount.allowed(Long.toString(accountId))) {
                return changeFail(ChangePasswordError.CHANGE_PASSWORD_RATE_LIMITED);
            }
            var found = accounts.findById(accountId);
            if (found.isEmpty()) {
                return changeFail(ChangePasswordError.CHANGE_PASSWORD_INTERNAL);
            }
            if (!AccountRules.validPassword(req.getOldPassword()) || !hasher.verify(req.getOldPassword(), found.get().passwordHash())) {
                changeByAccount.record(Long.toString(accountId));
                return changeFail(ChangePasswordError.WRONG_OLD_PASSWORD);
            }
            if (!AccountRules.validPassword(req.getNewPassword())) {
                return changeFail(ChangePasswordError.NEW_PASSWORD_INVALID);
            }
            accounts.changePassword(accountId, hasher.hash(req.getNewPassword()));
            changeByAccount.reset(Long.toString(accountId));
            String token = accounts.createRefreshToken(accountId, deviceInfo, refreshTokenDays);
            LOG.info("password changed for account {}", found.get().username());
            return ChangePasswordResult.newBuilder().setOk(true).setRefreshToken(token).build();
        } catch (SQLException e) {
            LOG.error("change password failed", e);
            return changeFail(ChangePasswordError.CHANGE_PASSWORD_INTERNAL);
        }
    }

    private static ChangePasswordResult changeFail(ChangePasswordError error) {
        return ChangePasswordResult.newBuilder().setOk(false).setError(error).build();
    }

    public LoginOutcome login(String ip, LoginRequest req) {
        try {
            if (req.getCredentialCase() == LoginRequest.CredentialCase.REFRESH_TOKEN) {
                return loginWithToken(ip, req.getRefreshToken());
            }
            if (req.getCredentialCase() == LoginRequest.CredentialCase.PASSWORD) {
                return loginWithPassword(ip, req);
            }
            return loginFail(LoginError.AUTH_FAILED);
        } catch (SQLException e) {
            LOG.error("login failed", e);
            return loginFail(LoginError.LOGIN_INTERNAL);
        }
    }

    private LoginOutcome loginWithPassword(String ip, LoginRequest req) throws SQLException {
        String username = req.getUsername();
        String password = req.getPassword();
        if (!AccountRules.validUsername(username) || !AccountRules.validPassword(password)) {
            loginByIp.record(ip);
            return loginFail(LoginError.AUTH_FAILED);
        }
        if (!loginByIp.allowed(ip) || !loginByUser.allowed(username)) {
            return loginFail(LoginError.AUTH_RATE_LIMITED);
        }
        var found = accounts.findByUsername(username);
        boolean good;
        if (found.isPresent()) {
            good = hasher.verify(password, found.get().passwordHash());
        } else {
            hasher.burn(password);
            good = false;
        }
        if (!good) {
            loginByIp.record(ip);
            loginByUser.record(username);
            return loginFail(LoginError.AUTH_FAILED);
        }
        AccountRepository.AccountRow account = found.get();
        if (account.banned()) {
            return loginFail(LoginError.ACCOUNT_BANNED);
        }
        loginByUser.reset(username);
        accounts.touchLogin(account.accountId());
        String token = accounts.createRefreshToken(account.accountId(), req.getDeviceInfo(), refreshTokenDays);
        return loginOk(account, token);
    }

    private LoginOutcome loginWithToken(String ip, String token) throws SQLException {
        if (!loginByIp.allowed(ip)) {
            return loginFail(LoginError.AUTH_RATE_LIMITED);
        }
        if (token == null || token.isBlank() || token.length() > 100) {
            loginByIp.record(ip);
            return loginFail(LoginError.AUTH_FAILED);
        }
        AccountRepository.TokenCheck check = accounts.checkRefreshToken(token);
        switch (check.state()) {
            case UNKNOWN -> {
                loginByIp.record(ip);
                return loginFail(LoginError.AUTH_FAILED);
            }
            case REVOKED -> {
                return loginFail(LoginError.TOKEN_REVOKED);
            }
            case EXPIRED -> {
                return loginFail(LoginError.TOKEN_EXPIRED);
            }
            default -> {
                AccountRepository.AccountRow account = check.login().account();
                if (account.banned()) {
                    return loginFail(LoginError.ACCOUNT_BANNED);
                }
                accounts.slideToken(check.login().tokenId(), refreshTokenDays);   // sliding 30 days (CONFIRMED)
                accounts.touchLogin(account.accountId());
                return loginOk(account, "");   // the existing token stays valid; a new one is issued only after a password login
            }
        }
    }

    private static LoginOutcome loginOk(AccountRepository.AccountRow account, String refreshToken) {
        LoginResult r = LoginResult.newBuilder()
                .setOk(true)
                .setAccountId(account.accountId())
                .setUsername(account.username())
                .setSessionId(UUID.randomUUID().toString())
                .setRefreshToken(refreshToken)
                .build();
        return new LoginOutcome(r, account.accountId());
    }

    private static LoginOutcome loginFail(LoginError error) {
        return new LoginOutcome(LoginResult.newBuilder().setOk(false).setError(error).build(), 0);
    }

    private static RegisterResult registerFail(RegisterError error) {
        return RegisterResult.newBuilder().setOk(false).setError(error).build();
    }
}
