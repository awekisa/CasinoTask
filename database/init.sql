CREATE ROLE casino_wallet LOGIN PASSWORD 'wallet_local';
CREATE ROLE casino_game LOGIN PASSWORD 'game_local';
CREATE DATABASE "Casino.Wallet.Database" OWNER casino_wallet;
CREATE DATABASE "Casino.Game.Database" OWNER casino_game;
REVOKE CONNECT ON DATABASE "Casino.Wallet.Database" FROM PUBLIC;
REVOKE CONNECT ON DATABASE "Casino.Game.Database" FROM PUBLIC;
GRANT CONNECT ON DATABASE "Casino.Wallet.Database" TO casino_wallet;
GRANT CONNECT ON DATABASE "Casino.Game.Database" TO casino_game;
