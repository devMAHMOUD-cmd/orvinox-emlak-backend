-- 1. Sistemdeki Kullanıcılar (Danışmanlar / Çalışanlar) Tablosu
CREATE TABLE Users (
    Id SERIAL PRIMARY KEY,
    Username VARCHAR(50) NOT NULL,
    Email VARCHAR(100) UNIQUE NOT NULL,
    PasswordHash VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 2. Müşteriler (Ev Sahipleri veya Alıcılar) Tablosu
CREATE TABLE Customers (
    Id SERIAL PRIMARY KEY,
    FirstName VARCHAR(50) NOT NULL,
    LastName VARCHAR(50) NOT NULL,
    Phone VARCHAR(20),
    Email VARCHAR(100),
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 3. Emlak İlanları Tablosu (Users ve Customers ile İlişkili)
CREATE TABLE Properties (
    Id SERIAL PRIMARY KEY,
    Title VARCHAR(150) NOT NULL,
    Description TEXT,
    Price DECIMAL(18,2) NOT NULL,
    PropertyType VARCHAR(20) NOT NULL, -- Örn: 'Satılık', 'Kiralık'
    Status VARCHAR(20) NOT NULL,       -- Örn: 'Aktif', 'Satıldı', 'Kiralandı'
    AgentId INT REFERENCES Users(Id) ON DELETE SET NULL,     -- İlanla ilgilenen danışman
    CustomerId INT REFERENCES Customers(Id) ON DELETE CASCADE, -- İlanın asıl sahibi
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);