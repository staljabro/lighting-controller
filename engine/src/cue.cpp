/*
  Q Light Controller Plus
  cue.cpp

  Copyright (c) Heikki Junnila
                Massimo Callegari

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0.txt

  Unless required by applicable law or agreed to in writing, software
  distributed under the License is distributed on an "AS IS" BASIS,
  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
  See the License for the specific language governing permissions and
  limitations under the License.
*/

#include <QXmlStreamReader>
#include <QXmlStreamWriter>
#include <QDebug>

#include "cue.h"

Cue::Cue(const QString& name)
    : m_name(name)
    , m_fadeInSpeed(0)
    , m_fadeOutSpeed(0)
    , m_duration(0)
    , m_delay(0)
    , m_follow(UINT_MAX)
{
}

Cue::Cue(const QMap <uint,uchar> values)
    : m_name(QString())
    , m_values(values)
    , m_fadeInSpeed(0)
    , m_fadeOutSpeed(0)
    , m_duration(0)
    , m_delay(0)
    , m_follow(UINT_MAX)
{
}

Cue::Cue(const Cue& cue)
    : m_name(cue.name())
    , m_number(cue.number())
    , m_notes(cue.notes())
    , m_values(cue.values())
    , m_fadeInSpeed(cue.fadeInSpeed())
    , m_fadeOutSpeed(cue.fadeOutSpeed())
    , m_duration(cue.duration())
    , m_delay(cue.delay())
    , m_follow(cue.follow())
{
}

Cue::~Cue()
{
}

Cue &Cue::operator=(const Cue &cue)
{
    if (this != &cue)
    {
        m_name = cue.name();
        m_number = cue.number();
        m_notes = cue.notes();
        m_values = cue.values();
        m_fadeInSpeed = cue.fadeInSpeed();
        m_fadeOutSpeed = cue.fadeOutSpeed();
        m_duration = cue.duration();
        m_delay = cue.delay();
        m_follow = cue.follow();
    }

    return *this;
}

/****************************************************************************
 * Name
 ****************************************************************************/

void Cue::setName(const QString& str)
{
    m_name = str;
}

QString Cue::name() const
{
    return m_name;
}

/****************************************************************************
 * Cue-list metadata
 ****************************************************************************/

void Cue::setNumber(const QString& number)
{
    m_number = number.trimmed();
}

QString Cue::number() const
{
    return m_number;
}

void Cue::setNotes(const QString& notes)
{
    m_notes = notes;
}

QString Cue::notes() const
{
    return m_notes;
}

/****************************************************************************
 * Values
 ****************************************************************************/

void Cue::setValue(uint channel, uchar value)
{
    m_values[channel] = value;
}

void Cue::unsetValue(uint channel)
{
    m_values.remove(channel);
}

bool Cue::containsValue(uint channel) const
{
    return m_values.contains(channel);
}

uchar Cue::value(uint channel) const
{
    return m_values.value(channel, 0);
}

QMap <uint,uchar> Cue::values() const
{
    return m_values;
}

/****************************************************************************
 * Speed
 ****************************************************************************/

void Cue::setFadeInSpeed(uint ms)
{
    m_fadeInSpeed = ms;
}

uint Cue::fadeInSpeed() const
{
    return m_fadeInSpeed;
}

void Cue::setFadeOutSpeed(uint ms)
{
    m_fadeOutSpeed = ms;
}

uint Cue::fadeOutSpeed() const
{
    return m_fadeOutSpeed;
}

void Cue::setDuration(uint ms)
{
    m_duration = ms;
}

uint Cue::duration() const
{
    return m_duration;
}

void Cue::setDelay(uint ms)
{
    m_delay = ms;
}

uint Cue::delay() const
{
    return m_delay;
}

void Cue::setFollow(uint ms)
{
    m_follow = ms;
}

uint Cue::follow() const
{
    return m_follow;
}

bool Cue::hasFollow() const
{
    return m_follow != UINT_MAX;
}

/****************************************************************************
 * Load & Save
 ****************************************************************************/

bool Cue::loadXML(QXmlStreamReader &root)
{
    qDebug() << Q_FUNC_INFO;

    if (root.name() != KXMLQLCCue)
    {
        qWarning() << Q_FUNC_INFO << "Cue node not found";
        return false;
    }

    setName(root.attributes().value(KXMLQLCCueName).toString());
    setNumber(root.attributes().value(KXMLQLCCueNumber).toString());
    setNotes(QString());
    setDelay(0);
    setFollow(UINT_MAX);

    while (root.readNextStartElement())
    {
        if (root.name() == KXMLQLCCueValue)
        {
            QString ch = root.attributes().value(KXMLQLCCueValueChannel).toString();
            QString val = root.readElementText();
            if (ch.isEmpty() == false && val.isEmpty() == false)
                setValue(ch.toUInt(), uchar(val.toUInt()));
        }
        else if (root.name() == KXMLQLCCueSpeed)
        {
            loadXMLSpeed(root);
        }
        else if (root.name() == KXMLQLCCueNotes)
        {
            setNotes(root.readElementText());
        }
        else
        {
            qWarning() << Q_FUNC_INFO << "Unrecognized Cue tag:" << root.name();
            root.skipCurrentElement();
        }
    }

    return true;
}

bool Cue::saveXML(QXmlStreamWriter *doc) const
{
    qDebug() << Q_FUNC_INFO;
    Q_ASSERT(doc != NULL);

    doc->writeStartElement(KXMLQLCCue);
    doc->writeAttribute(KXMLQLCCueName, name());
    if (number().isEmpty() == false)
        doc->writeAttribute(KXMLQLCCueNumber, number());

    if (notes().isEmpty() == false)
        doc->writeTextElement(KXMLQLCCueNotes, notes());

    QMapIterator <uint,uchar> it(values());
    while (it.hasNext() == true)
    {
        it.next();
        doc->writeStartElement(KXMLQLCCueValue);
        doc->writeAttribute(KXMLQLCCueValueChannel, QString::number(it.key()));
        doc->writeCharacters(QString::number(it.value()));
        doc->writeEndElement();
    }

    saveXMLSpeed(doc);

    /* End the <Cue> tag */
    doc->writeEndElement();

    return true;
}

bool Cue::loadXMLSpeed(QXmlStreamReader &speedRoot)
{
    if (speedRoot.name() != KXMLQLCCueSpeed)
        return false;

    m_fadeInSpeed = speedRoot.attributes().value(KXMLQLCCueSpeedFadeIn).toString().toUInt();
    m_fadeOutSpeed = speedRoot.attributes().value(KXMLQLCCueSpeedFadeOut).toString().toUInt();
    m_duration = speedRoot.attributes().value(KXMLQLCCueSpeedDuration).toString().toUInt();
    m_delay = speedRoot.attributes().value(KXMLQLCCueSpeedDelay).toString().toUInt();
    if (speedRoot.attributes().hasAttribute(KXMLQLCCueSpeedFollow))
        m_follow = speedRoot.attributes().value(KXMLQLCCueSpeedFollow).toString().toUInt();
    else
        m_follow = UINT_MAX;
    speedRoot.skipCurrentElement();

    return true;
}

bool Cue::saveXMLSpeed(QXmlStreamWriter *doc) const
{
    doc->writeStartElement(KXMLQLCCueSpeed);
    doc->writeAttribute(KXMLQLCCueSpeedFadeIn, QString::number(fadeInSpeed()));
    doc->writeAttribute(KXMLQLCCueSpeedFadeOut, QString::number(fadeOutSpeed()));
    doc->writeAttribute(KXMLQLCCueSpeedDuration, QString::number(duration()));
    doc->writeAttribute(KXMLQLCCueSpeedDelay, QString::number(delay()));
    if (hasFollow())
        doc->writeAttribute(KXMLQLCCueSpeedFollow, QString::number(follow()));
    doc->writeEndElement();

    return true;
}
