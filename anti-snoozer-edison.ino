
#include <Wire.h>
#include "rgb_lcd.h"

int ledPin_3 = 13;

//Setup message bytes

rgb_lcd lcd;

const int colorR = 255;
const int colorG = 0;
const int colorB = 0;

boolean toBlink = false;


void setup() {
  // put your setup code here, to run once:
  Serial.begin(9600); 
  
  lcd.begin(16, 2);
  pinMode(7, OUTPUT);
  pinMode(6, OUTPUT);
  
  pinMode(ledPin_3, OUTPUT);
 digitalWrite(ledPin_3, HIGH);//
  delay(250);//
  digitalWrite(ledPin_3, LOW);//
  delay(250);//

}

void loop() {

  
  // put your main code here, to run repeatedly: 
     if (Serial.available()>0) 
      /* read the most recent byte */
      {
        int tmp = Serial.parseInt();
        if(tmp == 0)
        {
          toBlink = false;
        }
        else if (tmp == 1)
        {
          toBlink = true;
        }
          Serial.println(tmp);

   }
  if(toBlink)
  {
    digitalWrite(7, HIGH);
    digitalWrite(6, HIGH);
    lcd.setRGB(0, colorG, colorB);
    //delay(analogRead(0));
    delay(1000);

    digitalWrite(7, LOW);
    digitalWrite(6, LOW);
    lcd.setRGB(colorR, colorG, colorB);
    //delay(analogRead(0));
    delay(1000);
  }
  else
  {
    lcd.setRGB(0, colorG, colorB);
    delay(1000);    
  }
}
