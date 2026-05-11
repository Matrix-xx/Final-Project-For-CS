-- =========================
-- Roles
-- =========================
CREATE TABLE Roles (
    id INT AUTO_INCREMENT PRIMARY KEY,
    role_name VARCHAR(50) NOT NULL
);

-- =========================
-- Registrations
-- =========================
CREATE TABLE Registrations (
    id INT AUTO_INCREMENT PRIMARY KEY,
    registration_name VARCHAR(100),
    birth_of_date DATE,
    email VARCHAR(100),
    password_hash VARCHAR(255),
    address TEXT,
    phone_no VARCHAR(20),
    photo VARCHAR(255),
    role_id INT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (role_id) REFERENCES Roles(id)
);

-- =========================
-- Resigns
-- =========================
CREATE TABLE Resigns (
    id INT AUTO_INCREMENT PRIMARY KEY,
    assignment_name VARCHAR(100),
    registration_id INT,
    reason TEXT,
    resign_at TIMESTAMP,
    FOREIGN KEY (registration_id) REFERENCES Registrations(id)
);

-- =========================
-- Categories
-- =========================
CREATE TABLE Categories (
    id INT AUTO_INCREMENT PRIMARY KEY,
    category_name VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- =========================
-- Recipes
-- =========================
CREATE TABLE Recipes (
    id INT AUTO_INCREMENT PRIMARY KEY,
    recipe_name VARCHAR(100),
    category_id INT,
    recipe_img VARCHAR(255),
    description TEXT,
    ingredients TEXT,
    price DECIMAL(10,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (category_id) REFERENCES Categories(id)
);

-- =========================
-- Inventories
-- =========================
CREATE TABLE Inventories (
    id INT AUTO_INCREMENT PRIMARY KEY,
    stock_qty INT DEFAULT 0,
    recipe_id INT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (recipe_id) REFERENCES Recipes(id)
);

-- =========================
-- Table Lists
-- =========================
CREATE TABLE Table_Lists (
    id INT AUTO_INCREMENT PRIMARY KEY,
    table_name VARCHAR(50),
    is_available TINYINT(1) NOT NULL DEFAULT 1,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- =========================
-- Status (Kitchen Status)
-- =========================
CREATE TABLE Status (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(50),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

-- =========================
-- Payment Methods
-- =========================
CREATE TABLE Payment_Methods (
    id INT AUTO_INCREMENT PRIMARY KEY,
    payment_name VARCHAR(50),
    note TEXT,
    is_deleted BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- =========================
-- Payments
-- =========================
CREATE TABLE Payments (
    id INT AUTO_INCREMENT PRIMARY KEY,
    payment_method_id INT,
    payment_status VARCHAR(20),
    amount DECIMAL(10,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (payment_method_id) REFERENCES Payment_Methods(id)
);

-- =========================
-- Orders
-- =========================
CREATE TABLE `Order` (
    id INT AUTO_INCREMENT PRIMARY KEY,
    payment_id INT,
    table_id INT,
    recipe_id INT,
    order_detail_id INT,
    status VARCHAR(20),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (payment_id) REFERENCES Payments(id),
    FOREIGN KEY (table_id) REFERENCES Table_Lists(id)
);

-- =========================
-- Order Detail
-- =========================
CREATE TABLE Order_Detail (
    id INT AUTO_INCREMENT PRIMARY KEY,
    table_id INT,
    recipe_id INT,
    qty INT,
    status_id INT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (table_id) REFERENCES Table_Lists(id),
    FOREIGN KEY (recipe_id) REFERENCES Recipes(id),
    FOREIGN KEY (status_id) REFERENCES Status(id)
);

-- schema-create-tables.sql
CREATE TABLE `tables` (
  `id` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  `table_number` INT NOT NULL UNIQUE,
  `qr_code` VARCHAR(512) NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;